package dev.coderig.rider.frontend;

import com.intellij.icons.AllIcons;
import com.intellij.ide.DataManager;
import com.intellij.openapi.actionSystem.ActionManager;
import com.intellij.openapi.actionSystem.DefaultActionGroup;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.ui.popup.JBPopupFactory;
import com.intellij.openapi.wm.StatusBar;
import com.intellij.openapi.wm.StatusBarWidget;
import com.intellij.openapi.wm.StatusBarWidgetFactory;
import com.intellij.ui.awt.RelativePoint;
import com.intellij.util.Consumer;
import java.awt.event.MouseEvent;
import javax.swing.Icon;

public final class CodeRigStatusWidgetFactory implements StatusBarWidgetFactory {
    static final String ID = "CodeRigStatus";

    @Override
    public String getId() {
        return ID;
    }

    @Override
    public String getDisplayName() {
        return "CodeRig resident index";
    }

    @Override
    public boolean isAvailable(Project project) {
        return project.getBasePath() != null;
    }

    @Override
    public StatusBarWidget createWidget(Project project) {
        return new Widget(project);
    }

    @Override
    public boolean canBeEnabledOn(StatusBar statusBar) {
        return true;
    }

    private static final class Widget implements StatusBarWidget, StatusBarWidget.IconPresentation {
        private final Project project;
        private final CodeRigStatusService service;
        private final Runnable listener = this::update;
        private StatusBar statusBar;

        private Widget(Project project) {
            this.project = project;
            service = project.getService(CodeRigStatusService.class);
            service.addListener(listener);
        }

        @Override
        public String ID() {
            return ID;
        }

        @Override
        public WidgetPresentation getPresentation() {
            return this;
        }

        @Override
        public void install(StatusBar statusBar) {
            this.statusBar = statusBar;
        }

        @Override
        public void dispose() {
            service.removeListener(listener);
            statusBar = null;
        }

        @Override
        public Icon getIcon() {
            return switch (service.snapshot().kind()) {
                case EXACT -> AllIcons.General.InspectionsOK;
                case STALE -> AllIcons.General.InspectionsWarning;
                case MISSING -> AllIcons.General.InspectionsTrafficOff;
                case RESTARTING, CHECKING -> AllIcons.Actions.Refresh;
                case ERROR -> AllIcons.General.InspectionsError;
            };
        }

        @Override
        public String getTooltipText() {
            return service.snapshot().tooltip();
        }

        @Override
        public Consumer<MouseEvent> getClickConsumer() {
            return this::showActions;
        }

        private void update() {
            if (statusBar != null) {
                statusBar.updateWidget(ID);
            }
        }

        private void showActions(MouseEvent event) {
            var manager = ActionManager.getInstance();
            var group = new DefaultActionGroup();
            group.add(manager.getAction("CodeRig.RefreshStatus"));
            group.add(manager.getAction("CodeRig.RestartWatch"));
            var dataContext = DataManager.getInstance().getDataContext(event.getComponent());
            var popup = JBPopupFactory.getInstance().createActionGroupPopup(
                "CodeRig — " + service.snapshot().shortText(),
                group,
                dataContext,
                JBPopupFactory.ActionSelectionAid.SPEEDSEARCH,
                true
            );
            popup.show(new RelativePoint(event));
        }
    }
}
