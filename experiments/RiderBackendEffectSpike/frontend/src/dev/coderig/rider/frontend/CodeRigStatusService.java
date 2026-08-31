package dev.coderig.rider.frontend;

import com.google.gson.Gson;
import com.intellij.openapi.Disposable;
import com.intellij.openapi.project.Project;
import com.intellij.util.concurrency.AppExecutorUtil;
import java.io.EOFException;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.net.ConnectException;
import java.net.StandardProtocolFamily;
import java.net.UnixDomainSocketAddress;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.channels.SocketChannel;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Duration;
import java.util.Comparator;
import java.util.HexFormat;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.stream.Stream;
import javax.swing.SwingUtilities;

public final class CodeRigStatusService implements Disposable {
    private static final int PROTOCOL = 1;
    private static final Duration IO_TIMEOUT = Duration.ofSeconds(2);
    private static final Duration RESTART_TIMEOUT = Duration.ofSeconds(45);
    private static final Gson JSON = new Gson();

    public enum Kind {
        CHECKING,
        EXACT,
        STALE,
        MISSING,
        RESTARTING,
        ERROR,
    }

    public record Snapshot(Kind kind, long generation, String detail) {
        public String shortText() {
            return switch (kind) {
                case CHECKING -> "checking";
                case EXACT -> "exact · generation " + generation;
                case STALE -> "stale";
                case MISSING -> "watch not running";
                case RESTARTING -> "restarting watch";
                case ERROR -> "error";
            };
        }

        public String tooltip() {
            var suffix = detail == null || detail.isBlank() ? "" : " — " + detail;
            return "CodeRig: " + shortText() + suffix + ". Click for actions.";
        }
    }

    private final Project project;
    private final ScheduledExecutorService executor;
    private final CopyOnWriteArrayList<Runnable> listeners = new CopyOnWriteArrayList<>();
    private final AtomicBoolean probing = new AtomicBoolean();
    private volatile Snapshot snapshot = new Snapshot(Kind.CHECKING, 0, "contacting rig watch");

    public CodeRigStatusService(Project project) {
        this.project = project;
        executor = AppExecutorUtil.createBoundedScheduledExecutorService("CodeRig status", 1);
        executor.scheduleWithFixedDelay(this::probeIfIdle, 0, 3, TimeUnit.SECONDS);
    }

    public Snapshot snapshot() {
        return snapshot;
    }

    public void addListener(Runnable listener) {
        listeners.add(listener);
    }

    public void removeListener(Runnable listener) {
        listeners.remove(listener);
    }

    public void refreshNow() {
        executor.execute(this::probeIfIdle);
    }

    public void restartWatch() {
        executor.execute(this::restartWatchInBackground);
    }

    @Override
    public void dispose() {
        executor.shutdownNow();
        listeners.clear();
    }

    private void probeIfIdle() {
        if (!probing.compareAndSet(false, true)) {
            return;
        }

        try {
            publish(probe());
        } finally {
            probing.set(false);
        }
    }

    private Snapshot probe() {
        var base = basePath();
        if (base == null) {
            return new Snapshot(Kind.ERROR, 0, "project has no local base path");
        }

        try {
            return snapshotFrom(send(base, "status"));
        } catch (FileNotFoundException | ConnectException exception) {
            return new Snapshot(Kind.MISSING, 0, "start it with CodeRig: Restart Watch");
        } catch (IOException exception) {
            if (!isWindows() && !Files.exists(endpointPath(base))) {
                return new Snapshot(Kind.MISSING, 0, "start it with CodeRig: Restart Watch");
            }
            return new Snapshot(Kind.ERROR, 0, concise(exception));
        } catch (RuntimeException exception) {
            return new Snapshot(Kind.ERROR, 0, concise(exception));
        }
    }

    private void restartWatchInBackground() {
        var base = basePath();
        if (base == null) {
            publish(new Snapshot(Kind.ERROR, 0, "project has no local base path"));
            return;
        }

        publish(new Snapshot(Kind.RESTARTING, 0, "requesting a clean host shutdown"));
        long previousHostProcessId = 0;
        try {
            try {
                var response = send(base, "restart");
                if (!response.restartAccepted) {
                    throw new IOException(response.reason == null ? "host declined restart" : response.reason);
                }
                previousHostProcessId = response.hostProcessId;
            } catch (FileNotFoundException | ConnectException exception) {
                // Missing is already stopped; proceed directly to starting the host.
            }

            waitForPreviousHost(base, previousHostProcessId, Duration.ofSeconds(8));
            startWatch(base);
            publish(new Snapshot(Kind.RESTARTING, 0, "cold-booting the resident index"));

            var deadline = System.nanoTime() + RESTART_TIMEOUT.toNanos();
            IOException lastFailure = null;
            while (System.nanoTime() < deadline) {
                try {
                    var next = snapshotFrom(send(base, "status"));
                    publish(next);
                    return;
                } catch (IOException exception) {
                    lastFailure = exception;
                    sleep(500);
                }
            }

            throw new IOException(
                "new rig watch did not become ready within " + RESTART_TIMEOUT.toSeconds() + "s",
                lastFailure
            );
        } catch (Exception exception) {
            publish(new Snapshot(Kind.ERROR, 0, concise(exception)));
        }
    }

    private void startWatch(Path base) throws IOException {
        var solution = findSolution(base);
        var rig = findRigExecutable();
        var logDirectory = base.resolve(".rig");
        Files.createDirectories(logDirectory);
        var log = logDirectory.resolve("rider-watch.log").toFile();
        var process = new ProcessBuilder(rig, "watch", solution.getFileName().toString())
            .directory(base.toFile())
            .redirectErrorStream(true)
            .redirectOutput(ProcessBuilder.Redirect.appendTo(log))
            .start();

        if (!process.isAlive()) {
            throw new IOException("rig watch exited before startup; see " + log.getPath());
        }
    }

    private static Path findSolution(Path base) throws IOException {
        try (Stream<Path> entries = Files.list(base)) {
            var solutions = entries
                .filter(path -> {
                    var name = path.getFileName().toString().toLowerCase(Locale.ROOT);
                    return name.endsWith(".slnx") || name.endsWith(".sln");
                })
                .sorted(Comparator.comparing(path -> path.getFileName().toString().toLowerCase(Locale.ROOT)))
                .toList();
            if (solutions.isEmpty()) {
                throw new IOException("no .slnx or .sln found in " + base);
            }
            return solutions.get(0);
        }
    }

    private static String findRigExecutable() {
        var executable = isWindows() ? "rig.exe" : "rig";
        var tool = Path.of(System.getProperty("user.home"), ".dotnet", "tools", executable);
        return Files.isExecutable(tool) || (isWindows() && Files.exists(tool)) ? tool.toString() : executable;
    }

    private Path basePath() {
        var value = project.getBasePath();
        return value == null || value.isBlank() ? null : Path.of(value).toAbsolutePath().normalize();
    }

    private static Snapshot snapshotFrom(ControlResponse response) throws IOException {
        if (response.protocol != PROTOCOL) {
            throw new IOException("protocol mismatch in watch-control response");
        }
        if (!"ok".equals(response.status)) {
            throw new IOException(response.reason == null || response.reason.isBlank() ? "host declined status" : response.reason);
        }
        if ("exact".equals(response.sourceStatus)) {
            return new Snapshot(
                Kind.EXACT,
                response.graphGeneration,
                "resident facts are current · pid " + response.hostProcessId
            );
        }
        return new Snapshot(
            Kind.STALE,
            response.graphGeneration,
            response.reason == null || response.reason.isBlank() ? "resident facts are not exact" : response.reason
        );
    }

    private static ControlResponse send(Path base, String action) throws IOException {
        var requestId = UUID.randomUUID().toString().replace("-", "");
        var request = new ControlRequest(PROTOCOL, "watch-control", base.toString(), requestId, action);
        var payload = JSON.toJson(request).getBytes(StandardCharsets.UTF_8);
        var responseBytes = isWindows() ? transactWindows(base, payload) : transactUnix(base, payload);
        var response = JSON.fromJson(new String(responseBytes, StandardCharsets.UTF_8), ControlResponse.class);
        if (response == null) {
            throw new EOFException("host returned an empty watch-control response");
        }
        if (!requestId.equals(response.requestId)) {
            throw new IOException("watch-control response did not match its request");
        }
        return response;
    }

    private static byte[] transactUnix(Path base, byte[] payload) throws IOException {
        var address = UnixDomainSocketAddress.of(endpointPath(base));
        try (var channel = SocketChannel.open(StandardProtocolFamily.UNIX)) {
            channel.configureBlocking(false);
            channel.connect(address);
            var deadline = System.nanoTime() + IO_TIMEOUT.toNanos();
            while (!channel.finishConnect()) {
                requireTime(deadline);
                sleep(5);
            }

            writeFully(channel, frame(payload), deadline);
            var header = ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN);
            readFully(channel, header, deadline);
            header.flip();
            var size = header.getInt();
            if (size <= 0 || size > 1024 * 1024) {
                throw new IOException("invalid watch-control frame length " + size);
            }
            var body = ByteBuffer.allocate(size);
            readFully(channel, body, deadline);
            return body.array();
        }
    }

    private static byte[] transactWindows(Path base, byte[] payload) throws IOException {
        try (var pipe = new RandomAccessFile(endpointPath(base).toString(), "rw")) {
            pipe.write(frame(payload).array());
            var header = new byte[4];
            pipe.readFully(header);
            var size = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN).getInt();
            if (size <= 0 || size > 1024 * 1024) {
                throw new IOException("invalid watch-control frame length " + size);
            }
            var body = new byte[size];
            pipe.readFully(body);
            return body;
        }
    }

    private static ByteBuffer frame(byte[] payload) {
        var frame = ByteBuffer.allocate(payload.length + 4).order(ByteOrder.LITTLE_ENDIAN);
        frame.putInt(payload.length).put(payload).flip();
        return frame;
    }

    private static void writeFully(SocketChannel channel, ByteBuffer buffer, long deadline) throws IOException {
        while (buffer.hasRemaining()) {
            if (channel.write(buffer) == 0) {
                requireTime(deadline);
                sleep(5);
            }
        }
    }

    private static void readFully(SocketChannel channel, ByteBuffer buffer, long deadline) throws IOException {
        while (buffer.hasRemaining()) {
            var read = channel.read(buffer);
            if (read < 0) {
                throw new EOFException("host closed the watch-control pipe");
            }
            if (read == 0) {
                requireTime(deadline);
                sleep(5);
            }
        }
    }

    private static void requireTime(long deadline) throws IOException {
        if (System.nanoTime() >= deadline) {
            throw new IOException("watch-control request timed out");
        }
    }

    private static void waitForPreviousHost(Path base, long processId, Duration timeout) throws IOException {
        var deadline = System.nanoTime() + timeout.toNanos();
        if (processId > 0) {
            while (ProcessHandle.of(processId).map(ProcessHandle::isAlive).orElse(false)) {
                if (System.nanoTime() >= deadline) {
                    throw new IOException("timed out waiting for rig watch process " + processId + " to exit");
                }
                sleep(50);
            }
        }

        if (isWindows()) {
            return; // never probe a Windows named pipe: opening it consumes one pending accept
        }
        while (Files.exists(endpointPath(base))) {
            if (System.nanoTime() >= deadline) {
                throw new IOException("timed out waiting for the previous rig watch endpoint to close");
            }
            sleep(50);
        }
    }

    private static Path endpointPath(Path base) {
        var pipeName = "rig-live-" + hash(normalize(base));
        if (isWindows()) {
            return Path.of("\\\\.\\pipe\\" + pipeName);
        }
        return Path.of(System.getProperty("java.io.tmpdir"), "CoreFxPipe_" + pipeName);
    }

    private static String normalize(Path base) {
        var value = base.toAbsolutePath().normalize().toString();
        if (isWindows()) {
            value = value.toLowerCase(Locale.ROOT);
        }
        return value;
    }

    private static String hash(String value) {
        try {
            var bytes = MessageDigest.getInstance("SHA-256").digest(value.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(bytes, 0, 8);
        } catch (NoSuchAlgorithmException exception) {
            throw new IllegalStateException(exception);
        }
    }

    private void publish(Snapshot next) {
        snapshot = next;
        SwingUtilities.invokeLater(() -> listeners.forEach(Runnable::run));
    }

    private static String concise(Throwable exception) {
        var message = exception.getMessage();
        return message == null || message.isBlank() ? exception.getClass().getSimpleName() : message;
    }

    private static void sleep(long milliseconds) {
        try {
            Thread.sleep(milliseconds);
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
        }
    }

    private static boolean isWindows() {
        return System.getProperty("os.name").toLowerCase(Locale.ROOT).contains("win");
    }

    private record ControlRequest(int protocol, String verb, String workingDirectory, String requestId, String action) { }

    private static final class ControlResponse {
        int protocol;
        String status;
        String requestId;
        String action;
        long hostProcessId;
        long graphGeneration;
        String sourceStatus;
        boolean restartAccepted;
        String reason;
    }
}
