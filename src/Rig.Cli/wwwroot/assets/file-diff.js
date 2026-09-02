//#region \0rolldown/runtime.js
var e = Object.defineProperty, t = (e, t) => () => (t || (e((t = { exports: {} }).exports, t), e = null), t.exports), n = (t, n) => {
	let r = {};
	for (var i in t) e(r, i, {
		get: t[i],
		enumerable: !0
	});
	return n || e(r, Symbol.toStringTag, { value: "Module" }), r;
}, r = /* @__PURE__ */ t(((e) => {
	var t = Symbol.for("react.transitional.element"), n = Symbol.for("react.portal"), r = Symbol.for("react.fragment"), i = Symbol.for("react.strict_mode"), a = Symbol.for("react.profiler"), o = Symbol.for("react.consumer"), s = Symbol.for("react.context"), c = Symbol.for("react.forward_ref"), l = Symbol.for("react.suspense"), u = Symbol.for("react.memo"), d = Symbol.for("react.lazy"), f = Symbol.for("react.activity"), p = Symbol.iterator;
	function m(e) {
		return typeof e != "object" || !e ? null : (e = p && e[p] || e["@@iterator"], typeof e == "function" ? e : null);
	}
	var h = {
		isMounted: function() {
			return !1;
		},
		enqueueForceUpdate: function() {},
		enqueueReplaceState: function() {},
		enqueueSetState: function() {}
	}, g = Object.assign, _ = {};
	function v(e, t, n) {
		this.props = e, this.context = t, this.refs = _, this.updater = n || h;
	}
	v.prototype.isReactComponent = {}, v.prototype.setState = function(e, t) {
		if (typeof e != "object" && typeof e != "function" && e != null) throw Error("takes an object of state variables to update or a function which returns an object of state variables.");
		this.updater.enqueueSetState(this, e, t, "setState");
	}, v.prototype.forceUpdate = function(e) {
		this.updater.enqueueForceUpdate(this, e, "forceUpdate");
	};
	function y() {}
	y.prototype = v.prototype;
	function b(e, t, n) {
		this.props = e, this.context = t, this.refs = _, this.updater = n || h;
	}
	var x = b.prototype = new y();
	x.constructor = b, g(x, v.prototype), x.isPureReactComponent = !0;
	var S = Array.isArray;
	function C() {}
	var w = {
		H: null,
		A: null,
		T: null,
		S: null
	}, T = Object.prototype.hasOwnProperty;
	function E(e, n, r) {
		var i = r.ref;
		return {
			$$typeof: t,
			type: e,
			key: n,
			ref: i === void 0 ? null : i,
			props: r
		};
	}
	function D(e, t) {
		return E(e.type, t, e.props);
	}
	function O(e) {
		return typeof e == "object" && !!e && e.$$typeof === t;
	}
	function k(e) {
		var t = {
			"=": "=0",
			":": "=2"
		};
		return "$" + e.replace(/[=:]/g, function(e) {
			return t[e];
		});
	}
	var A = /\/+/g;
	function j(e, t) {
		return typeof e == "object" && e && e.key != null ? k("" + e.key) : t.toString(36);
	}
	function M(e) {
		switch (e.status) {
			case "fulfilled": return e.value;
			case "rejected": throw e.reason;
			default: switch (typeof e.status == "string" ? e.then(C, C) : (e.status = "pending", e.then(function(t) {
				e.status === "pending" && (e.status = "fulfilled", e.value = t);
			}, function(t) {
				e.status === "pending" && (e.status = "rejected", e.reason = t);
			})), e.status) {
				case "fulfilled": return e.value;
				case "rejected": throw e.reason;
			}
		}
		throw e;
	}
	function ee(e, r, i, a, o) {
		var s = typeof e;
		(s === "undefined" || s === "boolean") && (e = null);
		var c = !1;
		if (e === null) c = !0;
		else switch (s) {
			case "bigint":
			case "string":
			case "number":
				c = !0;
				break;
			case "object": switch (e.$$typeof) {
				case t:
				case n:
					c = !0;
					break;
				case d: return c = e._init, ee(c(e._payload), r, i, a, o);
			}
		}
		if (c) return o = o(e), c = a === "" ? "." + j(e, 0) : a, S(o) ? (i = "", c != null && (i = c.replace(A, "$&/") + "/"), ee(o, r, i, "", function(e) {
			return e;
		})) : o != null && (O(o) && (o = D(o, i + (o.key == null || e && e.key === o.key ? "" : ("" + o.key).replace(A, "$&/") + "/") + c)), r.push(o)), 1;
		c = 0;
		var l = a === "" ? "." : a + ":";
		if (S(e)) for (var u = 0; u < e.length; u++) a = e[u], s = l + j(a, u), c += ee(a, r, i, s, o);
		else if (u = m(e), typeof u == "function") for (e = u.call(e), u = 0; !(a = e.next()).done;) a = a.value, s = l + j(a, u++), c += ee(a, r, i, s, o);
		else if (s === "object") {
			if (typeof e.then == "function") return ee(M(e), r, i, a, o);
			throw r = String(e), Error("Objects are not valid as a React child (found: " + (r === "[object Object]" ? "object with keys {" + Object.keys(e).join(", ") + "}" : r) + "). If you meant to render a collection of children, use an array instead.");
		}
		return c;
	}
	function te(e, t, n) {
		if (e == null) return e;
		var r = [], i = 0;
		return ee(e, r, "", "", function(e) {
			return t.call(n, e, i++);
		}), r;
	}
	function ne(e) {
		if (e._status === -1) {
			var t = e._result;
			t = t(), t.then(function(t) {
				(e._status === 0 || e._status === -1) && (e._status = 1, e._result = t);
			}, function(t) {
				(e._status === 0 || e._status === -1) && (e._status = 2, e._result = t);
			}), e._status === -1 && (e._status = 0, e._result = t);
		}
		if (e._status === 1) return e._result.default;
		throw e._result;
	}
	var N = typeof reportError == "function" ? reportError : function(e) {
		if (typeof window == "object" && typeof window.ErrorEvent == "function") {
			var t = new window.ErrorEvent("error", {
				bubbles: !0,
				cancelable: !0,
				message: typeof e == "object" && e && typeof e.message == "string" ? String(e.message) : String(e),
				error: e
			});
			if (!window.dispatchEvent(t)) return;
		} else if (typeof process == "object" && typeof process.emit == "function") {
			process.emit("uncaughtException", e);
			return;
		}
		console.error(e);
	}, P = {
		map: te,
		forEach: function(e, t, n) {
			te(e, function() {
				t.apply(this, arguments);
			}, n);
		},
		count: function(e) {
			var t = 0;
			return te(e, function() {
				t++;
			}), t;
		},
		toArray: function(e) {
			return te(e, function(e) {
				return e;
			}) || [];
		},
		only: function(e) {
			if (!O(e)) throw Error("React.Children.only expected to receive a single React element child.");
			return e;
		}
	};
	e.Activity = f, e.Children = P, e.Component = v, e.Fragment = r, e.Profiler = a, e.PureComponent = b, e.StrictMode = i, e.Suspense = l, e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = w, e.__COMPILER_RUNTIME = {
		__proto__: null,
		c: function(e) {
			return w.H.useMemoCache(e);
		}
	}, e.cache = function(e) {
		return function() {
			return e.apply(null, arguments);
		};
	}, e.cacheSignal = function() {
		return null;
	}, e.cloneElement = function(e, t, n) {
		if (e == null) throw Error("The argument must be a React element, but you passed " + e + ".");
		var r = g({}, e.props), i = e.key;
		if (t != null) for (a in t.key !== void 0 && (i = "" + t.key), t) !T.call(t, a) || a === "key" || a === "__self" || a === "__source" || a === "ref" && t.ref === void 0 || (r[a] = t[a]);
		var a = arguments.length - 2;
		if (a === 1) r.children = n;
		else if (1 < a) {
			for (var o = Array(a), s = 0; s < a; s++) o[s] = arguments[s + 2];
			r.children = o;
		}
		return E(e.type, i, r);
	}, e.createContext = function(e) {
		return e = {
			$$typeof: s,
			_currentValue: e,
			_currentValue2: e,
			_threadCount: 0,
			Provider: null,
			Consumer: null
		}, e.Provider = e, e.Consumer = {
			$$typeof: o,
			_context: e
		}, e;
	}, e.createElement = function(e, t, n) {
		var r, i = {}, a = null;
		if (t != null) for (r in t.key !== void 0 && (a = "" + t.key), t) T.call(t, r) && r !== "key" && r !== "__self" && r !== "__source" && (i[r] = t[r]);
		var o = arguments.length - 2;
		if (o === 1) i.children = n;
		else if (1 < o) {
			for (var s = Array(o), c = 0; c < o; c++) s[c] = arguments[c + 2];
			i.children = s;
		}
		if (e && e.defaultProps) for (r in o = e.defaultProps, o) i[r] === void 0 && (i[r] = o[r]);
		return E(e, a, i);
	}, e.createRef = function() {
		return { current: null };
	}, e.forwardRef = function(e) {
		return {
			$$typeof: c,
			render: e
		};
	}, e.isValidElement = O, e.lazy = function(e) {
		return {
			$$typeof: d,
			_payload: {
				_status: -1,
				_result: e
			},
			_init: ne
		};
	}, e.memo = function(e, t) {
		return {
			$$typeof: u,
			type: e,
			compare: t === void 0 ? null : t
		};
	}, e.startTransition = function(e) {
		var t = w.T, n = {};
		w.T = n;
		try {
			var r = e(), i = w.S;
			i !== null && i(n, r), typeof r == "object" && r && typeof r.then == "function" && r.then(C, N);
		} catch (e) {
			N(e);
		} finally {
			t !== null && n.types !== null && (t.types = n.types), w.T = t;
		}
	}, e.unstable_useCacheRefresh = function() {
		return w.H.useCacheRefresh();
	}, e.use = function(e) {
		return w.H.use(e);
	}, e.useActionState = function(e, t, n) {
		return w.H.useActionState(e, t, n);
	}, e.useCallback = function(e, t) {
		return w.H.useCallback(e, t);
	}, e.useContext = function(e) {
		return w.H.useContext(e);
	}, e.useDebugValue = function() {}, e.useDeferredValue = function(e, t) {
		return w.H.useDeferredValue(e, t);
	}, e.useEffect = function(e, t) {
		return w.H.useEffect(e, t);
	}, e.useEffectEvent = function(e) {
		return w.H.useEffectEvent(e);
	}, e.useId = function() {
		return w.H.useId();
	}, e.useImperativeHandle = function(e, t, n) {
		return w.H.useImperativeHandle(e, t, n);
	}, e.useInsertionEffect = function(e, t) {
		return w.H.useInsertionEffect(e, t);
	}, e.useLayoutEffect = function(e, t) {
		return w.H.useLayoutEffect(e, t);
	}, e.useMemo = function(e, t) {
		return w.H.useMemo(e, t);
	}, e.useOptimistic = function(e, t) {
		return w.H.useOptimistic(e, t);
	}, e.useReducer = function(e, t, n) {
		return w.H.useReducer(e, t, n);
	}, e.useRef = function(e) {
		return w.H.useRef(e);
	}, e.useState = function(e) {
		return w.H.useState(e);
	}, e.useSyncExternalStore = function(e, t, n) {
		return w.H.useSyncExternalStore(e, t, n);
	}, e.useTransition = function() {
		return w.H.useTransition();
	}, e.version = "19.2.8";
})), i = /* @__PURE__ */ t(((e, t) => {
	t.exports = r();
})), a = /* @__PURE__ */ t(((e) => {
	function t(e, t) {
		var n = e.length;
		e.push(t);
		a: for (; 0 < n;) {
			var r = n - 1 >>> 1, a = e[r];
			if (0 < i(a, t)) e[r] = t, e[n] = a, n = r;
			else break a;
		}
	}
	function n(e) {
		return e.length === 0 ? null : e[0];
	}
	function r(e) {
		if (e.length === 0) return null;
		var t = e[0], n = e.pop();
		if (n !== t) {
			e[0] = n;
			a: for (var r = 0, a = e.length, o = a >>> 1; r < o;) {
				var s = 2 * (r + 1) - 1, c = e[s], l = s + 1, u = e[l];
				if (0 > i(c, n)) l < a && 0 > i(u, c) ? (e[r] = u, e[l] = n, r = l) : (e[r] = c, e[s] = n, r = s);
				else if (l < a && 0 > i(u, n)) e[r] = u, e[l] = n, r = l;
				else break a;
			}
		}
		return t;
	}
	function i(e, t) {
		var n = e.sortIndex - t.sortIndex;
		return n === 0 ? e.id - t.id : n;
	}
	if (e.unstable_now = void 0, typeof performance == "object" && typeof performance.now == "function") {
		var a = performance;
		e.unstable_now = function() {
			return a.now();
		};
	} else {
		var o = Date, s = o.now();
		e.unstable_now = function() {
			return o.now() - s;
		};
	}
	var c = [], l = [], u = 1, d = null, f = 3, p = !1, m = !1, h = !1, g = !1, _ = typeof setTimeout == "function" ? setTimeout : null, v = typeof clearTimeout == "function" ? clearTimeout : null, y = typeof setImmediate < "u" ? setImmediate : null;
	function b(e) {
		for (var i = n(l); i !== null;) {
			if (i.callback === null) r(l);
			else if (i.startTime <= e) r(l), i.sortIndex = i.expirationTime, t(c, i);
			else break;
			i = n(l);
		}
	}
	function x(e) {
		if (h = !1, b(e), !m) {
			if (n(c) !== null) m = !0, S || (S = !0, O());
			else {
				var t = n(l);
				t !== null && j(x, t.startTime - e);
			}
		}
	}
	var S = !1, C = -1, w = 5, T = -1;
	function E() {
		return g ? !0 : !(e.unstable_now() - T < w);
	}
	function D() {
		if (g = !1, S) {
			var t = e.unstable_now();
			T = t;
			var i = !0;
			try {
				a: {
					m = !1, h && (h = !1, v(C), C = -1), p = !0;
					var a = f;
					try {
						b: {
							for (b(t), d = n(c); d !== null && !(d.expirationTime > t && E());) {
								var o = d.callback;
								if (typeof o == "function") {
									d.callback = null, f = d.priorityLevel;
									var s = o(d.expirationTime <= t);
									if (t = e.unstable_now(), typeof s == "function") {
										d.callback = s, b(t), i = !0;
										break b;
									}
									d === n(c) && r(c), b(t);
								} else r(c);
								d = n(c);
							}
							if (d !== null) i = !0;
							else {
								var u = n(l);
								u !== null && j(x, u.startTime - t), i = !1;
							}
						}
						break a;
					} finally {
						d = null, f = a, p = !1;
					}
					i = void 0;
				}
			} finally {
				i ? O() : S = !1;
			}
		}
	}
	var O;
	if (typeof y == "function") O = function() {
		y(D);
	};
	else if (typeof MessageChannel < "u") {
		var k = new MessageChannel(), A = k.port2;
		k.port1.onmessage = D, O = function() {
			A.postMessage(null);
		};
	} else O = function() {
		_(D, 0);
	};
	function j(t, n) {
		C = _(function() {
			t(e.unstable_now());
		}, n);
	}
	e.unstable_IdlePriority = 5, e.unstable_ImmediatePriority = 1, e.unstable_LowPriority = 4, e.unstable_NormalPriority = 3, e.unstable_Profiling = null, e.unstable_UserBlockingPriority = 2, e.unstable_cancelCallback = function(e) {
		e.callback = null;
	}, e.unstable_forceFrameRate = function(e) {
		0 > e || 125 < e ? console.error("forceFrameRate takes a positive int between 0 and 125, forcing frame rates higher than 125 fps is not supported") : w = 0 < e ? Math.floor(1e3 / e) : 5;
	}, e.unstable_getCurrentPriorityLevel = function() {
		return f;
	}, e.unstable_next = function(e) {
		switch (f) {
			case 1:
			case 2:
			case 3:
				var t = 3;
				break;
			default: t = f;
		}
		var n = f;
		f = t;
		try {
			return e();
		} finally {
			f = n;
		}
	}, e.unstable_requestPaint = function() {
		g = !0;
	}, e.unstable_runWithPriority = function(e, t) {
		switch (e) {
			case 1:
			case 2:
			case 3:
			case 4:
			case 5: break;
			default: e = 3;
		}
		var n = f;
		f = e;
		try {
			return t();
		} finally {
			f = n;
		}
	}, e.unstable_scheduleCallback = function(r, i, a) {
		var o = e.unstable_now();
		switch (typeof a == "object" && a ? (a = a.delay, a = typeof a == "number" && 0 < a ? o + a : o) : a = o, r) {
			case 1:
				var s = -1;
				break;
			case 2:
				s = 250;
				break;
			case 5:
				s = 1073741823;
				break;
			case 4:
				s = 1e4;
				break;
			default: s = 5e3;
		}
		return s = a + s, r = {
			id: u++,
			callback: i,
			priorityLevel: r,
			startTime: a,
			expirationTime: s,
			sortIndex: -1
		}, a > o ? (r.sortIndex = a, t(l, r), n(c) === null && r === n(l) && (h ? (v(C), C = -1) : h = !0, j(x, a - o))) : (r.sortIndex = s, t(c, r), m || p || (m = !0, S || (S = !0, O()))), r;
	}, e.unstable_shouldYield = E, e.unstable_wrapCallback = function(e) {
		var t = f;
		return function() {
			var n = f;
			f = t;
			try {
				return e.apply(this, arguments);
			} finally {
				f = n;
			}
		};
	};
})), o = /* @__PURE__ */ t(((e, t) => {
	t.exports = a();
})), s = /* @__PURE__ */ t(((e) => {
	var t = i();
	function n(e) {
		var t = "https://react.dev/errors/" + e;
		if (1 < arguments.length) {
			t += "?args[]=" + encodeURIComponent(arguments[1]);
			for (var n = 2; n < arguments.length; n++) t += "&args[]=" + encodeURIComponent(arguments[n]);
		}
		return "Minified React error #" + e + "; visit " + t + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
	}
	function r() {}
	var a = {
		d: {
			f: r,
			r: function() {
				throw Error(n(522));
			},
			D: r,
			C: r,
			L: r,
			m: r,
			X: r,
			S: r,
			M: r
		},
		p: 0,
		findDOMNode: null
	}, o = Symbol.for("react.portal");
	function s(e, t, n) {
		var r = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
		return {
			$$typeof: o,
			key: r == null ? null : "" + r,
			children: e,
			containerInfo: t,
			implementation: n
		};
	}
	var c = t.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
	function l(e, t) {
		if (e === "font") return "";
		if (typeof t == "string") return t === "use-credentials" ? t : "";
	}
	e.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = a, e.createPortal = function(e, t) {
		var r = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
		if (!t || t.nodeType !== 1 && t.nodeType !== 9 && t.nodeType !== 11) throw Error(n(299));
		return s(e, t, null, r);
	}, e.flushSync = function(e) {
		var t = c.T, n = a.p;
		try {
			if (c.T = null, a.p = 2, e) return e();
		} finally {
			c.T = t, a.p = n, a.d.f();
		}
	}, e.preconnect = function(e, t) {
		typeof e == "string" && (t ? (t = t.crossOrigin, t = typeof t == "string" ? t === "use-credentials" ? t : "" : void 0) : t = null, a.d.C(e, t));
	}, e.prefetchDNS = function(e) {
		typeof e == "string" && a.d.D(e);
	}, e.preinit = function(e, t) {
		if (typeof e == "string" && t && typeof t.as == "string") {
			var n = t.as, r = l(n, t.crossOrigin), i = typeof t.integrity == "string" ? t.integrity : void 0, o = typeof t.fetchPriority == "string" ? t.fetchPriority : void 0;
			n === "style" ? a.d.S(e, typeof t.precedence == "string" ? t.precedence : void 0, {
				crossOrigin: r,
				integrity: i,
				fetchPriority: o
			}) : n === "script" && a.d.X(e, {
				crossOrigin: r,
				integrity: i,
				fetchPriority: o,
				nonce: typeof t.nonce == "string" ? t.nonce : void 0
			});
		}
	}, e.preinitModule = function(e, t) {
		if (typeof e == "string") {
			if (typeof t == "object" && t) {
				if (t.as == null || t.as === "script") {
					var n = l(t.as, t.crossOrigin);
					a.d.M(e, {
						crossOrigin: n,
						integrity: typeof t.integrity == "string" ? t.integrity : void 0,
						nonce: typeof t.nonce == "string" ? t.nonce : void 0
					});
				}
			} else t ?? a.d.M(e);
		}
	}, e.preload = function(e, t) {
		if (typeof e == "string" && typeof t == "object" && t && typeof t.as == "string") {
			var n = t.as, r = l(n, t.crossOrigin);
			a.d.L(e, n, {
				crossOrigin: r,
				integrity: typeof t.integrity == "string" ? t.integrity : void 0,
				nonce: typeof t.nonce == "string" ? t.nonce : void 0,
				type: typeof t.type == "string" ? t.type : void 0,
				fetchPriority: typeof t.fetchPriority == "string" ? t.fetchPriority : void 0,
				referrerPolicy: typeof t.referrerPolicy == "string" ? t.referrerPolicy : void 0,
				imageSrcSet: typeof t.imageSrcSet == "string" ? t.imageSrcSet : void 0,
				imageSizes: typeof t.imageSizes == "string" ? t.imageSizes : void 0,
				media: typeof t.media == "string" ? t.media : void 0
			});
		}
	}, e.preloadModule = function(e, t) {
		if (typeof e == "string") {
			if (t) {
				var n = l(t.as, t.crossOrigin);
				a.d.m(e, {
					as: typeof t.as == "string" && t.as !== "script" ? t.as : void 0,
					crossOrigin: n,
					integrity: typeof t.integrity == "string" ? t.integrity : void 0
				});
			} else a.d.m(e);
		}
	}, e.requestFormReset = function(e) {
		a.d.r(e);
	}, e.unstable_batchedUpdates = function(e, t) {
		return e(t);
	}, e.useFormState = function(e, t, n) {
		return c.H.useFormState(e, t, n);
	}, e.useFormStatus = function() {
		return c.H.useHostTransitionStatus();
	}, e.version = "19.2.8";
})), c = /* @__PURE__ */ t(((e, t) => {
	function n() {
		if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function")) try {
			__REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(n);
		} catch (e) {
			console.error(e);
		}
	}
	n(), t.exports = s();
})), l = /* @__PURE__ */ t(((e) => {
	var t = o(), n = i(), r = c();
	function a(e) {
		var t = "https://react.dev/errors/" + e;
		if (1 < arguments.length) {
			t += "?args[]=" + encodeURIComponent(arguments[1]);
			for (var n = 2; n < arguments.length; n++) t += "&args[]=" + encodeURIComponent(arguments[n]);
		}
		return "Minified React error #" + e + "; visit " + t + " for the full message or use the non-minified dev environment for full errors and additional helpful warnings.";
	}
	function s(e) {
		return !(!e || e.nodeType !== 1 && e.nodeType !== 9 && e.nodeType !== 11);
	}
	function l(e) {
		var t = e, n = e;
		if (e.alternate) for (; t.return;) t = t.return;
		else {
			e = t;
			do
				t = e, t.flags & 4098 && (n = t.return), e = t.return;
			while (e);
		}
		return t.tag === 3 ? n : null;
	}
	function u(e) {
		if (e.tag === 13) {
			var t = e.memoizedState;
			if (t === null && (e = e.alternate, e !== null && (t = e.memoizedState)), t !== null) return t.dehydrated;
		}
		return null;
	}
	function d(e) {
		if (e.tag === 31) {
			var t = e.memoizedState;
			if (t === null && (e = e.alternate, e !== null && (t = e.memoizedState)), t !== null) return t.dehydrated;
		}
		return null;
	}
	function f(e) {
		if (l(e) !== e) throw Error(a(188));
	}
	function p(e) {
		var t = e.alternate;
		if (!t) {
			if (t = l(e), t === null) throw Error(a(188));
			return t === e ? e : null;
		}
		for (var n = e, r = t;;) {
			var i = n.return;
			if (i === null) break;
			var o = i.alternate;
			if (o === null) {
				if (r = i.return, r !== null) {
					n = r;
					continue;
				}
				break;
			}
			if (i.child === o.child) {
				for (o = i.child; o;) {
					if (o === n) return f(i), e;
					if (o === r) return f(i), t;
					o = o.sibling;
				}
				throw Error(a(188));
			}
			if (n.return !== r.return) n = i, r = o;
			else {
				for (var s = !1, c = i.child; c;) {
					if (c === n) {
						s = !0, n = i, r = o;
						break;
					}
					if (c === r) {
						s = !0, r = i, n = o;
						break;
					}
					c = c.sibling;
				}
				if (!s) {
					for (c = o.child; c;) {
						if (c === n) {
							s = !0, n = o, r = i;
							break;
						}
						if (c === r) {
							s = !0, r = o, n = i;
							break;
						}
						c = c.sibling;
					}
					if (!s) throw Error(a(189));
				}
			}
			if (n.alternate !== r) throw Error(a(190));
		}
		if (n.tag !== 3) throw Error(a(188));
		return n.stateNode.current === n ? e : t;
	}
	function m(e) {
		var t = e.tag;
		if (t === 5 || t === 26 || t === 27 || t === 6) return e;
		for (e = e.child; e !== null;) {
			if (t = m(e), t !== null) return t;
			e = e.sibling;
		}
		return null;
	}
	var h = Object.assign, g = Symbol.for("react.element"), _ = Symbol.for("react.transitional.element"), v = Symbol.for("react.portal"), y = Symbol.for("react.fragment"), b = Symbol.for("react.strict_mode"), x = Symbol.for("react.profiler"), S = Symbol.for("react.consumer"), C = Symbol.for("react.context"), w = Symbol.for("react.forward_ref"), T = Symbol.for("react.suspense"), E = Symbol.for("react.suspense_list"), D = Symbol.for("react.memo"), O = Symbol.for("react.lazy"), k = Symbol.for("react.activity"), A = Symbol.for("react.memo_cache_sentinel"), j = Symbol.iterator;
	function M(e) {
		return typeof e != "object" || !e ? null : (e = j && e[j] || e["@@iterator"], typeof e == "function" ? e : null);
	}
	var ee = Symbol.for("react.client.reference");
	function te(e) {
		if (e == null) return null;
		if (typeof e == "function") return e.$$typeof === ee ? null : e.displayName || e.name || null;
		if (typeof e == "string") return e;
		switch (e) {
			case y: return "Fragment";
			case x: return "Profiler";
			case b: return "StrictMode";
			case T: return "Suspense";
			case E: return "SuspenseList";
			case k: return "Activity";
		}
		if (typeof e == "object") switch (e.$$typeof) {
			case v: return "Portal";
			case C: return e.displayName || "Context";
			case S: return (e._context.displayName || "Context") + ".Consumer";
			case w:
				var t = e.render;
				return e = e.displayName, e ||= (e = t.displayName || t.name || "", e === "" ? "ForwardRef" : "ForwardRef(" + e + ")"), e;
			case D: return t = e.displayName || null, t === null ? te(e.type) || "Memo" : t;
			case O:
				t = e._payload, e = e._init;
				try {
					return te(e(t));
				} catch {}
		}
		return null;
	}
	var ne = Array.isArray, N = n.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, P = r.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, re = {
		pending: !1,
		data: null,
		method: null,
		action: null
	}, ie = [], ae = -1;
	function oe(e) {
		return { current: e };
	}
	function F(e) {
		0 > ae || (e.current = ie[ae], ie[ae] = null, ae--);
	}
	function I(e, t) {
		ae++, ie[ae] = e.current, e.current = t;
	}
	var L = oe(null), se = oe(null), ce = oe(null), le = oe(null);
	function ue(e, t) {
		switch (I(ce, t), I(se, e), I(L, null), t.nodeType) {
			case 9:
			case 11:
				e = (e = t.documentElement) && (e = e.namespaceURI) ? Vd(e) : 0;
				break;
			default: if (e = t.tagName, t = t.namespaceURI) t = Vd(t), e = Hd(t, e);
			else switch (e) {
				case "svg":
					e = 1;
					break;
				case "math":
					e = 2;
					break;
				default: e = 0;
			}
		}
		F(L), I(L, e);
	}
	function de() {
		F(L), F(se), F(ce);
	}
	function fe(e) {
		e.memoizedState !== null && I(le, e);
		var t = L.current, n = Hd(t, e.type);
		t !== n && (I(se, e), I(L, n));
	}
	function pe(e) {
		se.current === e && (F(L), F(se)), le.current === e && (F(le), Qf._currentValue = re);
	}
	var me, he;
	function ge(e) {
		if (me === void 0) try {
			throw Error();
		} catch (e) {
			var t = e.stack.trim().match(/\n( *(at )?)/);
			me = t && t[1] || "", he = -1 < e.stack.indexOf("\n    at") ? " (<anonymous>)" : -1 < e.stack.indexOf("@") ? "@unknown:0:0" : "";
		}
		return "\n" + me + e + he;
	}
	var _e = !1;
	function ve(e, t) {
		if (!e || _e) return "";
		_e = !0;
		var n = Error.prepareStackTrace;
		Error.prepareStackTrace = void 0;
		try {
			var r = { DetermineComponentFrameRoot: function() {
				try {
					if (t) {
						var n = function() {
							throw Error();
						};
						if (Object.defineProperty(n.prototype, "props", { set: function() {
							throw Error();
						} }), typeof Reflect == "object" && Reflect.construct) {
							try {
								Reflect.construct(n, []);
							} catch (e) {
								var r = e;
							}
							Reflect.construct(e, [], n);
						} else {
							try {
								n.call();
							} catch (e) {
								r = e;
							}
							e.call(n.prototype);
						}
					} else {
						try {
							throw Error();
						} catch (e) {
							r = e;
						}
						(n = e()) && typeof n.catch == "function" && n.catch(function() {});
					}
				} catch (e) {
					if (e && r && typeof e.stack == "string") return [e.stack, r.stack];
				}
				return [null, null];
			} };
			r.DetermineComponentFrameRoot.displayName = "DetermineComponentFrameRoot";
			var i = Object.getOwnPropertyDescriptor(r.DetermineComponentFrameRoot, "name");
			i && i.configurable && Object.defineProperty(r.DetermineComponentFrameRoot, "name", { value: "DetermineComponentFrameRoot" });
			var a = r.DetermineComponentFrameRoot(), o = a[0], s = a[1];
			if (o && s) {
				var c = o.split("\n"), l = s.split("\n");
				for (i = r = 0; r < c.length && !c[r].includes("DetermineComponentFrameRoot");) r++;
				for (; i < l.length && !l[i].includes("DetermineComponentFrameRoot");) i++;
				if (r === c.length || i === l.length) for (r = c.length - 1, i = l.length - 1; 1 <= r && 0 <= i && c[r] !== l[i];) i--;
				for (; 1 <= r && 0 <= i; r--, i--) if (c[r] !== l[i]) {
					if (r !== 1 || i !== 1) do
						if (r--, i--, 0 > i || c[r] !== l[i]) {
							var u = "\n" + c[r].replace(" at new ", " at ");
							return e.displayName && u.includes("<anonymous>") && (u = u.replace("<anonymous>", e.displayName)), u;
						}
					while (1 <= r && 0 <= i);
					break;
				}
			}
		} finally {
			_e = !1, Error.prepareStackTrace = n;
		}
		return (n = e ? e.displayName || e.name : "") ? ge(n) : "";
	}
	function ye(e, t) {
		switch (e.tag) {
			case 26:
			case 27:
			case 5: return ge(e.type);
			case 16: return ge("Lazy");
			case 13: return e.child !== t && t !== null ? ge("Suspense Fallback") : ge("Suspense");
			case 19: return ge("SuspenseList");
			case 0:
			case 15: return ve(e.type, !1);
			case 11: return ve(e.type.render, !1);
			case 1: return ve(e.type, !0);
			case 31: return ge("Activity");
			default: return "";
		}
	}
	function be(e) {
		try {
			var t = "", n = null;
			do
				t += ye(e, n), n = e, e = e.return;
			while (e);
			return t;
		} catch (e) {
			return "\nError generating stack: " + e.message + "\n" + e.stack;
		}
	}
	var xe = Object.prototype.hasOwnProperty, Se = t.unstable_scheduleCallback, Ce = t.unstable_cancelCallback, we = t.unstable_shouldYield, Te = t.unstable_requestPaint, Ee = t.unstable_now, De = t.unstable_getCurrentPriorityLevel, Oe = t.unstable_ImmediatePriority, ke = t.unstable_UserBlockingPriority, Ae = t.unstable_NormalPriority, je = t.unstable_LowPriority, Me = t.unstable_IdlePriority, Ne = t.log, Pe = t.unstable_setDisableYieldValue, Fe = null, Ie = null;
	function Le(e) {
		if (typeof Ne == "function" && Pe(e), Ie && typeof Ie.setStrictMode == "function") try {
			Ie.setStrictMode(Fe, e);
		} catch {}
	}
	var Re = Math.clz32 ? Math.clz32 : Ve, ze = Math.log, Be = Math.LN2;
	function Ve(e) {
		return e >>>= 0, e === 0 ? 32 : 31 - (ze(e) / Be | 0) | 0;
	}
	var He = 256, Ue = 262144, We = 4194304;
	function Ge(e) {
		var t = e & 42;
		if (t !== 0) return t;
		switch (e & -e) {
			case 1: return 1;
			case 2: return 2;
			case 4: return 4;
			case 8: return 8;
			case 16: return 16;
			case 32: return 32;
			case 64: return 64;
			case 128: return 128;
			case 256:
			case 512:
			case 1024:
			case 2048:
			case 4096:
			case 8192:
			case 16384:
			case 32768:
			case 65536:
			case 131072: return e & 261888;
			case 262144:
			case 524288:
			case 1048576:
			case 2097152: return e & 3932160;
			case 4194304:
			case 8388608:
			case 16777216:
			case 33554432: return e & 62914560;
			case 67108864: return 67108864;
			case 134217728: return 134217728;
			case 268435456: return 268435456;
			case 536870912: return 536870912;
			case 1073741824: return 0;
			default: return e;
		}
	}
	function Ke(e, t, n) {
		var r = e.pendingLanes;
		if (r === 0) return 0;
		var i = 0, a = e.suspendedLanes, o = e.pingedLanes;
		e = e.warmLanes;
		var s = r & 134217727;
		return s === 0 ? (s = r & ~a, s === 0 ? o === 0 ? n || (n = r & ~e, n !== 0 && (i = Ge(n))) : i = Ge(o) : i = Ge(s)) : (r = s & ~a, r === 0 ? (o &= s, o === 0 ? n || (n = s & ~e, n !== 0 && (i = Ge(n))) : i = Ge(o)) : i = Ge(r)), i === 0 ? 0 : t !== 0 && t !== i && (t & a) === 0 && (a = i & -i, n = t & -t, a >= n || a === 32 && n & 4194048) ? t : i;
	}
	function qe(e, t) {
		return (e.pendingLanes & ~(e.suspendedLanes & ~e.pingedLanes) & t) === 0;
	}
	function Je(e, t) {
		switch (e) {
			case 1:
			case 2:
			case 4:
			case 8:
			case 64: return t + 250;
			case 16:
			case 32:
			case 128:
			case 256:
			case 512:
			case 1024:
			case 2048:
			case 4096:
			case 8192:
			case 16384:
			case 32768:
			case 65536:
			case 131072:
			case 262144:
			case 524288:
			case 1048576:
			case 2097152: return t + 5e3;
			case 4194304:
			case 8388608:
			case 16777216:
			case 33554432: return -1;
			case 67108864:
			case 134217728:
			case 268435456:
			case 536870912:
			case 1073741824: return -1;
			default: return -1;
		}
	}
	function Ye() {
		var e = We;
		return We <<= 1, !(We & 62914560) && (We = 4194304), e;
	}
	function Xe(e) {
		for (var t = [], n = 0; 31 > n; n++) t.push(e);
		return t;
	}
	function Ze(e, t) {
		e.pendingLanes |= t, t !== 268435456 && (e.suspendedLanes = 0, e.pingedLanes = 0, e.warmLanes = 0);
	}
	function Qe(e, t, n, r, i, a) {
		var o = e.pendingLanes;
		e.pendingLanes = n, e.suspendedLanes = 0, e.pingedLanes = 0, e.warmLanes = 0, e.expiredLanes &= n, e.entangledLanes &= n, e.errorRecoveryDisabledLanes &= n, e.shellSuspendCounter = 0;
		var s = e.entanglements, c = e.expirationTimes, l = e.hiddenUpdates;
		for (n = o & ~n; 0 < n;) {
			var u = 31 - Re(n), d = 1 << u;
			s[u] = 0, c[u] = -1;
			var f = l[u];
			if (f !== null) for (l[u] = null, u = 0; u < f.length; u++) {
				var p = f[u];
				p !== null && (p.lane &= -536870913);
			}
			n &= ~d;
		}
		r !== 0 && $e(e, r, 0), a !== 0 && i === 0 && e.tag !== 0 && (e.suspendedLanes |= a & ~(o & ~t));
	}
	function $e(e, t, n) {
		e.pendingLanes |= t, e.suspendedLanes &= ~t;
		var r = 31 - Re(t);
		e.entangledLanes |= t, e.entanglements[r] = e.entanglements[r] | 1073741824 | n & 261930;
	}
	function et(e, t) {
		var n = e.entangledLanes |= t;
		for (e = e.entanglements; n;) {
			var r = 31 - Re(n), i = 1 << r;
			i & t | e[r] & t && (e[r] |= t), n &= ~i;
		}
	}
	function tt(e, t) {
		var n = t & -t;
		return n = n & 42 ? 1 : nt(n), (n & (e.suspendedLanes | t)) === 0 ? n : 0;
	}
	function nt(e) {
		switch (e) {
			case 2:
				e = 1;
				break;
			case 8:
				e = 4;
				break;
			case 32:
				e = 16;
				break;
			case 256:
			case 512:
			case 1024:
			case 2048:
			case 4096:
			case 8192:
			case 16384:
			case 32768:
			case 65536:
			case 131072:
			case 262144:
			case 524288:
			case 1048576:
			case 2097152:
			case 4194304:
			case 8388608:
			case 16777216:
			case 33554432:
				e = 128;
				break;
			case 268435456:
				e = 134217728;
				break;
			default: e = 0;
		}
		return e;
	}
	function rt(e) {
		return e &= -e, 2 < e ? 8 < e ? e & 134217727 ? 32 : 268435456 : 8 : 2;
	}
	function it() {
		var e = P.p;
		return e === 0 ? (e = window.event, e === void 0 ? 32 : mp(e.type)) : e;
	}
	function at(e, t) {
		var n = P.p;
		try {
			return P.p = e, t();
		} finally {
			P.p = n;
		}
	}
	var ot = Math.random().toString(36).slice(2), st = "__reactFiber$" + ot, ct = "__reactProps$" + ot, lt = "__reactContainer$" + ot, ut = "__reactEvents$" + ot, dt = "__reactListeners$" + ot, ft = "__reactHandles$" + ot, pt = "__reactResources$" + ot, mt = "__reactMarker$" + ot;
	function ht(e) {
		delete e[st], delete e[ct], delete e[ut], delete e[dt], delete e[ft];
	}
	function gt(e) {
		var t = e[st];
		if (t) return t;
		for (var n = e.parentNode; n;) {
			if (t = n[lt] || n[st]) {
				if (n = t.alternate, t.child !== null || n !== null && n.child !== null) for (e = df(e); e !== null;) {
					if (n = e[st]) return n;
					e = df(e);
				}
				return t;
			}
			e = n, n = e.parentNode;
		}
		return null;
	}
	function _t(e) {
		if (e = e[st] || e[lt]) {
			var t = e.tag;
			if (t === 5 || t === 6 || t === 13 || t === 31 || t === 26 || t === 27 || t === 3) return e;
		}
		return null;
	}
	function vt(e) {
		var t = e.tag;
		if (t === 5 || t === 26 || t === 27 || t === 6) return e.stateNode;
		throw Error(a(33));
	}
	function yt(e) {
		var t = e[pt];
		return t ||= e[pt] = {
			hoistableStyles: /* @__PURE__ */ new Map(),
			hoistableScripts: /* @__PURE__ */ new Map()
		}, t;
	}
	function bt(e) {
		e[mt] = !0;
	}
	var xt = /* @__PURE__ */ new Set(), St = {};
	function Ct(e, t) {
		wt(e, t), wt(e + "Capture", t);
	}
	function wt(e, t) {
		for (St[e] = t, e = 0; e < t.length; e++) xt.add(t[e]);
	}
	var Tt = RegExp("^[:A-Z_a-z\\u00C0-\\u00D6\\u00D8-\\u00F6\\u00F8-\\u02FF\\u0370-\\u037D\\u037F-\\u1FFF\\u200C-\\u200D\\u2070-\\u218F\\u2C00-\\u2FEF\\u3001-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFFD][:A-Z_a-z\\u00C0-\\u00D6\\u00D8-\\u00F6\\u00F8-\\u02FF\\u0370-\\u037D\\u037F-\\u1FFF\\u200C-\\u200D\\u2070-\\u218F\\u2C00-\\u2FEF\\u3001-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFFD\\-.0-9\\u00B7\\u0300-\\u036F\\u203F-\\u2040]*$"), Et = {}, Dt = {};
	function Ot(e) {
		return xe.call(Dt, e) ? !0 : xe.call(Et, e) ? !1 : Tt.test(e) ? Dt[e] = !0 : (Et[e] = !0, !1);
	}
	function kt(e, t, n) {
		if (Ot(t)) {
			if (n === null) e.removeAttribute(t);
			else {
				switch (typeof n) {
					case "undefined":
					case "function":
					case "symbol":
						e.removeAttribute(t);
						return;
					case "boolean":
						var r = t.toLowerCase().slice(0, 5);
						if (r !== "data-" && r !== "aria-") {
							e.removeAttribute(t);
							return;
						}
				}
				e.setAttribute(t, "" + n);
			}
		}
	}
	function At(e, t, n) {
		if (n === null) e.removeAttribute(t);
		else {
			switch (typeof n) {
				case "undefined":
				case "function":
				case "symbol":
				case "boolean":
					e.removeAttribute(t);
					return;
			}
			e.setAttribute(t, "" + n);
		}
	}
	function jt(e, t, n, r) {
		if (r === null) e.removeAttribute(n);
		else {
			switch (typeof r) {
				case "undefined":
				case "function":
				case "symbol":
				case "boolean":
					e.removeAttribute(n);
					return;
			}
			e.setAttributeNS(t, n, "" + r);
		}
	}
	function Mt(e) {
		switch (typeof e) {
			case "bigint":
			case "boolean":
			case "number":
			case "string":
			case "undefined": return e;
			case "object": return e;
			default: return "";
		}
	}
	function Nt(e) {
		var t = e.type;
		return (e = e.nodeName) && e.toLowerCase() === "input" && (t === "checkbox" || t === "radio");
	}
	function Pt(e, t, n) {
		var r = Object.getOwnPropertyDescriptor(e.constructor.prototype, t);
		if (!e.hasOwnProperty(t) && r !== void 0 && typeof r.get == "function" && typeof r.set == "function") {
			var i = r.get, a = r.set;
			return Object.defineProperty(e, t, {
				configurable: !0,
				get: function() {
					return i.call(this);
				},
				set: function(e) {
					n = "" + e, a.call(this, e);
				}
			}), Object.defineProperty(e, t, { enumerable: r.enumerable }), {
				getValue: function() {
					return n;
				},
				setValue: function(e) {
					n = "" + e;
				},
				stopTracking: function() {
					e._valueTracker = null, delete e[t];
				}
			};
		}
	}
	function Ft(e) {
		if (!e._valueTracker) {
			var t = Nt(e) ? "checked" : "value";
			e._valueTracker = Pt(e, t, "" + e[t]);
		}
	}
	function It(e) {
		if (!e) return !1;
		var t = e._valueTracker;
		if (!t) return !0;
		var n = t.getValue(), r = "";
		return e && (r = Nt(e) ? e.checked ? "true" : "false" : e.value), e = r, e !== n && (t.setValue(e), !0);
	}
	function Lt(e) {
		if (e ||= typeof document < "u" ? document : void 0, e === void 0) return null;
		try {
			return e.activeElement || e.body;
		} catch {
			return e.body;
		}
	}
	var Rt = /[\n"\\]/g;
	function zt(e) {
		return e.replace(Rt, function(e) {
			return "\\" + e.charCodeAt(0).toString(16) + " ";
		});
	}
	function Bt(e, t, n, r, i, a, o, s) {
		e.name = "", o != null && typeof o != "function" && typeof o != "symbol" && typeof o != "boolean" ? e.type = o : e.removeAttribute("type"), t == null ? o !== "submit" && o !== "reset" || e.removeAttribute("value") : o === "number" ? (t === 0 && e.value === "" || e.value != t) && (e.value = "" + Mt(t)) : e.value !== "" + Mt(t) && (e.value = "" + Mt(t)), t == null ? n == null ? r != null && e.removeAttribute("value") : Ht(e, o, Mt(n)) : Ht(e, o, Mt(t)), i == null && a != null && (e.defaultChecked = !!a), i != null && (e.checked = i && typeof i != "function" && typeof i != "symbol"), s != null && typeof s != "function" && typeof s != "symbol" && typeof s != "boolean" ? e.name = "" + Mt(s) : e.removeAttribute("name");
	}
	function Vt(e, t, n, r, i, a, o, s) {
		if (a != null && typeof a != "function" && typeof a != "symbol" && typeof a != "boolean" && (e.type = a), t != null || n != null) {
			if (!(a !== "submit" && a !== "reset" || t != null)) {
				Ft(e);
				return;
			}
			n = n == null ? "" : "" + Mt(n), t = t == null ? n : "" + Mt(t), s || t === e.value || (e.value = t), e.defaultValue = t;
		}
		r ??= i, r = typeof r != "function" && typeof r != "symbol" && !!r, e.checked = s ? e.checked : !!r, e.defaultChecked = !!r, o != null && typeof o != "function" && typeof o != "symbol" && typeof o != "boolean" && (e.name = o), Ft(e);
	}
	function Ht(e, t, n) {
		t === "number" && Lt(e.ownerDocument) === e || e.defaultValue === "" + n || (e.defaultValue = "" + n);
	}
	function Ut(e, t, n, r) {
		if (e = e.options, t) {
			t = {};
			for (var i = 0; i < n.length; i++) t["$" + n[i]] = !0;
			for (n = 0; n < e.length; n++) i = t.hasOwnProperty("$" + e[n].value), e[n].selected !== i && (e[n].selected = i), i && r && (e[n].defaultSelected = !0);
		} else {
			for (n = "" + Mt(n), t = null, i = 0; i < e.length; i++) {
				if (e[i].value === n) {
					e[i].selected = !0, r && (e[i].defaultSelected = !0);
					return;
				}
				t !== null || e[i].disabled || (t = e[i]);
			}
			t !== null && (t.selected = !0);
		}
	}
	function Wt(e, t, n) {
		if (t != null && (t = "" + Mt(t), t !== e.value && (e.value = t), n == null)) {
			e.defaultValue !== t && (e.defaultValue = t);
			return;
		}
		e.defaultValue = n == null ? "" : "" + Mt(n);
	}
	function Gt(e, t, n, r) {
		if (t == null) {
			if (r != null) {
				if (n != null) throw Error(a(92));
				if (ne(r)) {
					if (1 < r.length) throw Error(a(93));
					r = r[0];
				}
				n = r;
			}
			n ??= "", t = n;
		}
		n = Mt(t), e.defaultValue = n, r = e.textContent, r === n && r !== "" && r !== null && (e.value = r), Ft(e);
	}
	function Kt(e, t) {
		if (t) {
			var n = e.firstChild;
			if (n && n === e.lastChild && n.nodeType === 3) {
				n.nodeValue = t;
				return;
			}
		}
		e.textContent = t;
	}
	var qt = new Set("animationIterationCount aspectRatio borderImageOutset borderImageSlice borderImageWidth boxFlex boxFlexGroup boxOrdinalGroup columnCount columns flex flexGrow flexPositive flexShrink flexNegative flexOrder gridArea gridRow gridRowEnd gridRowSpan gridRowStart gridColumn gridColumnEnd gridColumnSpan gridColumnStart fontWeight lineClamp lineHeight opacity order orphans scale tabSize widows zIndex zoom fillOpacity floodOpacity stopOpacity strokeDasharray strokeDashoffset strokeMiterlimit strokeOpacity strokeWidth MozAnimationIterationCount MozBoxFlex MozBoxFlexGroup MozLineClamp msAnimationIterationCount msFlex msZoom msFlexGrow msFlexNegative msFlexOrder msFlexPositive msFlexShrink msGridColumn msGridColumnSpan msGridRow msGridRowSpan WebkitAnimationIterationCount WebkitBoxFlex WebKitBoxFlexGroup WebkitBoxOrdinalGroup WebkitColumnCount WebkitColumns WebkitFlex WebkitFlexGrow WebkitFlexPositive WebkitFlexShrink WebkitLineClamp".split(" "));
	function Jt(e, t, n) {
		var r = t.indexOf("--") === 0;
		n == null || typeof n == "boolean" || n === "" ? r ? e.setProperty(t, "") : t === "float" ? e.cssFloat = "" : e[t] = "" : r ? e.setProperty(t, n) : typeof n != "number" || n === 0 || qt.has(t) ? t === "float" ? e.cssFloat = n : e[t] = ("" + n).trim() : e[t] = n + "px";
	}
	function Yt(e, t, n) {
		if (t != null && typeof t != "object") throw Error(a(62));
		if (e = e.style, n != null) {
			for (var r in n) !n.hasOwnProperty(r) || t != null && t.hasOwnProperty(r) || (r.indexOf("--") === 0 ? e.setProperty(r, "") : r === "float" ? e.cssFloat = "" : e[r] = "");
			for (var i in t) r = t[i], t.hasOwnProperty(i) && n[i] !== r && Jt(e, i, r);
		} else for (var o in t) t.hasOwnProperty(o) && Jt(e, o, t[o]);
	}
	function Xt(e) {
		if (e.indexOf("-") === -1) return !1;
		switch (e) {
			case "annotation-xml":
			case "color-profile":
			case "font-face":
			case "font-face-src":
			case "font-face-uri":
			case "font-face-format":
			case "font-face-name":
			case "missing-glyph": return !1;
			default: return !0;
		}
	}
	var Zt = /* @__PURE__ */ new Map([
		["acceptCharset", "accept-charset"],
		["htmlFor", "for"],
		["httpEquiv", "http-equiv"],
		["crossOrigin", "crossorigin"],
		["accentHeight", "accent-height"],
		["alignmentBaseline", "alignment-baseline"],
		["arabicForm", "arabic-form"],
		["baselineShift", "baseline-shift"],
		["capHeight", "cap-height"],
		["clipPath", "clip-path"],
		["clipRule", "clip-rule"],
		["colorInterpolation", "color-interpolation"],
		["colorInterpolationFilters", "color-interpolation-filters"],
		["colorProfile", "color-profile"],
		["colorRendering", "color-rendering"],
		["dominantBaseline", "dominant-baseline"],
		["enableBackground", "enable-background"],
		["fillOpacity", "fill-opacity"],
		["fillRule", "fill-rule"],
		["floodColor", "flood-color"],
		["floodOpacity", "flood-opacity"],
		["fontFamily", "font-family"],
		["fontSize", "font-size"],
		["fontSizeAdjust", "font-size-adjust"],
		["fontStretch", "font-stretch"],
		["fontStyle", "font-style"],
		["fontVariant", "font-variant"],
		["fontWeight", "font-weight"],
		["glyphName", "glyph-name"],
		["glyphOrientationHorizontal", "glyph-orientation-horizontal"],
		["glyphOrientationVertical", "glyph-orientation-vertical"],
		["horizAdvX", "horiz-adv-x"],
		["horizOriginX", "horiz-origin-x"],
		["imageRendering", "image-rendering"],
		["letterSpacing", "letter-spacing"],
		["lightingColor", "lighting-color"],
		["markerEnd", "marker-end"],
		["markerMid", "marker-mid"],
		["markerStart", "marker-start"],
		["overlinePosition", "overline-position"],
		["overlineThickness", "overline-thickness"],
		["paintOrder", "paint-order"],
		["panose-1", "panose-1"],
		["pointerEvents", "pointer-events"],
		["renderingIntent", "rendering-intent"],
		["shapeRendering", "shape-rendering"],
		["stopColor", "stop-color"],
		["stopOpacity", "stop-opacity"],
		["strikethroughPosition", "strikethrough-position"],
		["strikethroughThickness", "strikethrough-thickness"],
		["strokeDasharray", "stroke-dasharray"],
		["strokeDashoffset", "stroke-dashoffset"],
		["strokeLinecap", "stroke-linecap"],
		["strokeLinejoin", "stroke-linejoin"],
		["strokeMiterlimit", "stroke-miterlimit"],
		["strokeOpacity", "stroke-opacity"],
		["strokeWidth", "stroke-width"],
		["textAnchor", "text-anchor"],
		["textDecoration", "text-decoration"],
		["textRendering", "text-rendering"],
		["transformOrigin", "transform-origin"],
		["underlinePosition", "underline-position"],
		["underlineThickness", "underline-thickness"],
		["unicodeBidi", "unicode-bidi"],
		["unicodeRange", "unicode-range"],
		["unitsPerEm", "units-per-em"],
		["vAlphabetic", "v-alphabetic"],
		["vHanging", "v-hanging"],
		["vIdeographic", "v-ideographic"],
		["vMathematical", "v-mathematical"],
		["vectorEffect", "vector-effect"],
		["vertAdvY", "vert-adv-y"],
		["vertOriginX", "vert-origin-x"],
		["vertOriginY", "vert-origin-y"],
		["wordSpacing", "word-spacing"],
		["writingMode", "writing-mode"],
		["xmlnsXlink", "xmlns:xlink"],
		["xHeight", "x-height"]
	]), Qt = /^[\u0000-\u001F ]*j[\r\n\t]*a[\r\n\t]*v[\r\n\t]*a[\r\n\t]*s[\r\n\t]*c[\r\n\t]*r[\r\n\t]*i[\r\n\t]*p[\r\n\t]*t[\r\n\t]*:/i;
	function $t(e) {
		return Qt.test("" + e) ? "javascript:throw new Error('React has blocked a javascript: URL as a security precaution.')" : e;
	}
	function en() {}
	var tn = null;
	function nn(e) {
		return e = e.target || e.srcElement || window, e.correspondingUseElement && (e = e.correspondingUseElement), e.nodeType === 3 ? e.parentNode : e;
	}
	var rn = null, R = null;
	function an(e) {
		var t = _t(e);
		if (t && (e = t.stateNode)) {
			var n = e[ct] || null;
			a: switch (e = t.stateNode, t.type) {
				case "input":
					if (Bt(e, n.value, n.defaultValue, n.defaultValue, n.checked, n.defaultChecked, n.type, n.name), t = n.name, n.type === "radio" && t != null) {
						for (n = e; n.parentNode;) n = n.parentNode;
						for (n = n.querySelectorAll("input[name=\"" + zt("" + t) + "\"][type=\"radio\"]"), t = 0; t < n.length; t++) {
							var r = n[t];
							if (r !== e && r.form === e.form) {
								var i = r[ct] || null;
								if (!i) throw Error(a(90));
								Bt(r, i.value, i.defaultValue, i.defaultValue, i.checked, i.defaultChecked, i.type, i.name);
							}
						}
						for (t = 0; t < n.length; t++) r = n[t], r.form === e.form && It(r);
					}
					break a;
				case "textarea":
					Wt(e, n.value, n.defaultValue);
					break a;
				case "select": t = n.value, t != null && Ut(e, !!n.multiple, t, !1);
			}
		}
	}
	var on = !1;
	function sn(e, t, n) {
		if (on) return e(t, n);
		on = !0;
		try {
			return e(t);
		} finally {
			if (on = !1, (rn !== null || R !== null) && (bu(), rn && (t = rn, e = R, R = rn = null, an(t), e))) for (t = 0; t < e.length; t++) an(e[t]);
		}
	}
	function cn(e, t) {
		var n = e.stateNode;
		if (n === null) return null;
		var r = n[ct] || null;
		if (r === null) return null;
		n = r[t];
		a: switch (t) {
			case "onClick":
			case "onClickCapture":
			case "onDoubleClick":
			case "onDoubleClickCapture":
			case "onMouseDown":
			case "onMouseDownCapture":
			case "onMouseMove":
			case "onMouseMoveCapture":
			case "onMouseUp":
			case "onMouseUpCapture":
			case "onMouseEnter":
				(r = !r.disabled) || (e = e.type, r = e !== "button" && e !== "input" && e !== "select" && e !== "textarea"), e = !r;
				break a;
			default: e = !1;
		}
		if (e) return null;
		if (n && typeof n != "function") throw Error(a(231, t, typeof n));
		return n;
	}
	var ln = !(typeof window > "u" || window.document === void 0 || window.document.createElement === void 0), un = !1;
	if (ln) try {
		var dn = {};
		Object.defineProperty(dn, "passive", { get: function() {
			un = !0;
		} }), window.addEventListener("test", dn, dn), window.removeEventListener("test", dn, dn);
	} catch {
		un = !1;
	}
	var fn = null, pn = null, mn = null;
	function hn() {
		if (mn) return mn;
		var e, t = pn, n = t.length, r, i = "value" in fn ? fn.value : fn.textContent, a = i.length;
		for (e = 0; e < n && t[e] === i[e]; e++);
		var o = n - e;
		for (r = 1; r <= o && t[n - r] === i[a - r]; r++);
		return mn = i.slice(e, 1 < r ? 1 - r : void 0);
	}
	function gn(e) {
		var t = e.keyCode;
		return "charCode" in e ? (e = e.charCode, e === 0 && t === 13 && (e = 13)) : e = t, e === 10 && (e = 13), 32 <= e || e === 13 ? e : 0;
	}
	function _n() {
		return !0;
	}
	function vn() {
		return !1;
	}
	function yn(e) {
		function t(t, n, r, i, a) {
			for (var o in this._reactName = t, this._targetInst = r, this.type = n, this.nativeEvent = i, this.target = a, this.currentTarget = null, e) e.hasOwnProperty(o) && (t = e[o], this[o] = t ? t(i) : i[o]);
			return this.isDefaultPrevented = (i.defaultPrevented == null ? !1 === i.returnValue : i.defaultPrevented) ? _n : vn, this.isPropagationStopped = vn, this;
		}
		return h(t.prototype, {
			preventDefault: function() {
				this.defaultPrevented = !0;
				var e = this.nativeEvent;
				e && (e.preventDefault ? e.preventDefault() : typeof e.returnValue != "unknown" && (e.returnValue = !1), this.isDefaultPrevented = _n);
			},
			stopPropagation: function() {
				var e = this.nativeEvent;
				e && (e.stopPropagation ? e.stopPropagation() : typeof e.cancelBubble != "unknown" && (e.cancelBubble = !0), this.isPropagationStopped = _n);
			},
			persist: function() {},
			isPersistent: _n
		}), t;
	}
	var bn = {
		eventPhase: 0,
		bubbles: 0,
		cancelable: 0,
		timeStamp: function(e) {
			return e.timeStamp || Date.now();
		},
		defaultPrevented: 0,
		isTrusted: 0
	}, xn = yn(bn), Sn = h({}, bn, {
		view: 0,
		detail: 0
	}), Cn = yn(Sn), wn, Tn, En, Dn = h({}, Sn, {
		screenX: 0,
		screenY: 0,
		clientX: 0,
		clientY: 0,
		pageX: 0,
		pageY: 0,
		ctrlKey: 0,
		shiftKey: 0,
		altKey: 0,
		metaKey: 0,
		getModifierState: Rn,
		button: 0,
		buttons: 0,
		relatedTarget: function(e) {
			return e.relatedTarget === void 0 ? e.fromElement === e.srcElement ? e.toElement : e.fromElement : e.relatedTarget;
		},
		movementX: function(e) {
			return "movementX" in e ? e.movementX : (e !== En && (En && e.type === "mousemove" ? (wn = e.screenX - En.screenX, Tn = e.screenY - En.screenY) : Tn = wn = 0, En = e), wn);
		},
		movementY: function(e) {
			return "movementY" in e ? e.movementY : Tn;
		}
	}), On = yn(Dn), kn = yn(h({}, Dn, { dataTransfer: 0 })), An = yn(h({}, Sn, { relatedTarget: 0 })), jn = yn(h({}, bn, {
		animationName: 0,
		elapsedTime: 0,
		pseudoElement: 0
	})), Mn = yn(h({}, bn, { clipboardData: function(e) {
		return "clipboardData" in e ? e.clipboardData : window.clipboardData;
	} })), Nn = yn(h({}, bn, { data: 0 })), Pn = {
		Esc: "Escape",
		Spacebar: " ",
		Left: "ArrowLeft",
		Up: "ArrowUp",
		Right: "ArrowRight",
		Down: "ArrowDown",
		Del: "Delete",
		Win: "OS",
		Menu: "ContextMenu",
		Apps: "ContextMenu",
		Scroll: "ScrollLock",
		MozPrintableKey: "Unidentified"
	}, Fn = {
		8: "Backspace",
		9: "Tab",
		12: "Clear",
		13: "Enter",
		16: "Shift",
		17: "Control",
		18: "Alt",
		19: "Pause",
		20: "CapsLock",
		27: "Escape",
		32: " ",
		33: "PageUp",
		34: "PageDown",
		35: "End",
		36: "Home",
		37: "ArrowLeft",
		38: "ArrowUp",
		39: "ArrowRight",
		40: "ArrowDown",
		45: "Insert",
		46: "Delete",
		112: "F1",
		113: "F2",
		114: "F3",
		115: "F4",
		116: "F5",
		117: "F6",
		118: "F7",
		119: "F8",
		120: "F9",
		121: "F10",
		122: "F11",
		123: "F12",
		144: "NumLock",
		145: "ScrollLock",
		224: "Meta"
	}, In = {
		Alt: "altKey",
		Control: "ctrlKey",
		Meta: "metaKey",
		Shift: "shiftKey"
	};
	function Ln(e) {
		var t = this.nativeEvent;
		return t.getModifierState ? t.getModifierState(e) : (e = In[e]) ? !!t[e] : !1;
	}
	function Rn() {
		return Ln;
	}
	var zn = yn(h({}, Sn, {
		key: function(e) {
			if (e.key) {
				var t = Pn[e.key] || e.key;
				if (t !== "Unidentified") return t;
			}
			return e.type === "keypress" ? (e = gn(e), e === 13 ? "Enter" : String.fromCharCode(e)) : e.type === "keydown" || e.type === "keyup" ? Fn[e.keyCode] || "Unidentified" : "";
		},
		code: 0,
		location: 0,
		ctrlKey: 0,
		shiftKey: 0,
		altKey: 0,
		metaKey: 0,
		repeat: 0,
		locale: 0,
		getModifierState: Rn,
		charCode: function(e) {
			return e.type === "keypress" ? gn(e) : 0;
		},
		keyCode: function(e) {
			return e.type === "keydown" || e.type === "keyup" ? e.keyCode : 0;
		},
		which: function(e) {
			return e.type === "keypress" ? gn(e) : e.type === "keydown" || e.type === "keyup" ? e.keyCode : 0;
		}
	})), Bn = yn(h({}, Dn, {
		pointerId: 0,
		width: 0,
		height: 0,
		pressure: 0,
		tangentialPressure: 0,
		tiltX: 0,
		tiltY: 0,
		twist: 0,
		pointerType: 0,
		isPrimary: 0
	})), Vn = yn(h({}, Sn, {
		touches: 0,
		targetTouches: 0,
		changedTouches: 0,
		altKey: 0,
		metaKey: 0,
		ctrlKey: 0,
		shiftKey: 0,
		getModifierState: Rn
	})), Hn = yn(h({}, bn, {
		propertyName: 0,
		elapsedTime: 0,
		pseudoElement: 0
	})), Un = yn(h({}, Dn, {
		deltaX: function(e) {
			return "deltaX" in e ? e.deltaX : "wheelDeltaX" in e ? -e.wheelDeltaX : 0;
		},
		deltaY: function(e) {
			return "deltaY" in e ? e.deltaY : "wheelDeltaY" in e ? -e.wheelDeltaY : "wheelDelta" in e ? -e.wheelDelta : 0;
		},
		deltaZ: 0,
		deltaMode: 0
	})), Wn = yn(h({}, bn, {
		newState: 0,
		oldState: 0
	})), Gn = [
		9,
		13,
		27,
		32
	], Kn = ln && "CompositionEvent" in window, qn = null;
	ln && "documentMode" in document && (qn = document.documentMode);
	var Jn = ln && "TextEvent" in window && !qn, Yn = ln && (!Kn || qn && 8 < qn && 11 >= qn), Xn = " ", Zn = !1;
	function Qn(e, t) {
		switch (e) {
			case "keyup": return Gn.indexOf(t.keyCode) !== -1;
			case "keydown": return t.keyCode !== 229;
			case "keypress":
			case "mousedown":
			case "focusout": return !0;
			default: return !1;
		}
	}
	function $n(e) {
		return e = e.detail, typeof e == "object" && "data" in e ? e.data : null;
	}
	var er = !1;
	function tr(e, t) {
		switch (e) {
			case "compositionend": return $n(t);
			case "keypress": return t.which === 32 ? (Zn = !0, Xn) : null;
			case "textInput": return e = t.data, e === Xn && Zn ? null : e;
			default: return null;
		}
	}
	function nr(e, t) {
		if (er) return e === "compositionend" || !Kn && Qn(e, t) ? (e = hn(), mn = pn = fn = null, er = !1, e) : null;
		switch (e) {
			case "paste": return null;
			case "keypress":
				if (!(t.ctrlKey || t.altKey || t.metaKey) || t.ctrlKey && t.altKey) {
					if (t.char && 1 < t.char.length) return t.char;
					if (t.which) return String.fromCharCode(t.which);
				}
				return null;
			case "compositionend": return Yn && t.locale !== "ko" ? null : t.data;
			default: return null;
		}
	}
	var rr = {
		color: !0,
		date: !0,
		datetime: !0,
		"datetime-local": !0,
		email: !0,
		month: !0,
		number: !0,
		password: !0,
		range: !0,
		search: !0,
		tel: !0,
		text: !0,
		time: !0,
		url: !0,
		week: !0
	};
	function ir(e) {
		var t = e && e.nodeName && e.nodeName.toLowerCase();
		return t === "input" ? !!rr[e.type] : t === "textarea";
	}
	function ar(e, t, n, r) {
		rn ? R ? R.push(r) : R = [r] : rn = r, t = Ed(t, "onChange"), 0 < t.length && (n = new xn("onChange", "change", null, n, r), e.push({
			event: n,
			listeners: t
		}));
	}
	var or = null, sr = null;
	function cr(e) {
		yd(e, 0);
	}
	function lr(e) {
		if (It(vt(e))) return e;
	}
	function ur(e, t) {
		if (e === "change") return t;
	}
	var dr = !1;
	if (ln) {
		var fr;
		if (ln) {
			var pr = "oninput" in document;
			if (!pr) {
				var mr = document.createElement("div");
				mr.setAttribute("oninput", "return;"), pr = typeof mr.oninput == "function";
			}
			fr = pr;
		} else fr = !1;
		dr = fr && (!document.documentMode || 9 < document.documentMode);
	}
	function hr() {
		or && (or.detachEvent("onpropertychange", gr), sr = or = null);
	}
	function gr(e) {
		if (e.propertyName === "value" && lr(sr)) {
			var t = [];
			ar(t, sr, e, nn(e)), sn(cr, t);
		}
	}
	function _r(e, t, n) {
		e === "focusin" ? (hr(), or = t, sr = n, or.attachEvent("onpropertychange", gr)) : e === "focusout" && hr();
	}
	function vr(e) {
		if (e === "selectionchange" || e === "keyup" || e === "keydown") return lr(sr);
	}
	function yr(e, t) {
		if (e === "click") return lr(t);
	}
	function br(e, t) {
		if (e === "input" || e === "change") return lr(t);
	}
	function xr(e, t) {
		return e === t && (e !== 0 || 1 / e == 1 / t) || e !== e && t !== t;
	}
	var Sr = typeof Object.is == "function" ? Object.is : xr;
	function Cr(e, t) {
		if (Sr(e, t)) return !0;
		if (typeof e != "object" || !e || typeof t != "object" || !t) return !1;
		var n = Object.keys(e), r = Object.keys(t);
		if (n.length !== r.length) return !1;
		for (r = 0; r < n.length; r++) {
			var i = n[r];
			if (!xe.call(t, i) || !Sr(e[i], t[i])) return !1;
		}
		return !0;
	}
	function wr(e) {
		for (; e && e.firstChild;) e = e.firstChild;
		return e;
	}
	function Tr(e, t) {
		var n = wr(e);
		e = 0;
		for (var r; n;) {
			if (n.nodeType === 3) {
				if (r = e + n.textContent.length, e <= t && r >= t) return {
					node: n,
					offset: t - e
				};
				e = r;
			}
			a: {
				for (; n;) {
					if (n.nextSibling) {
						n = n.nextSibling;
						break a;
					}
					n = n.parentNode;
				}
				n = void 0;
			}
			n = wr(n);
		}
	}
	function Er(e, t) {
		return e && t ? e === t ? !0 : e && e.nodeType === 3 ? !1 : t && t.nodeType === 3 ? Er(e, t.parentNode) : "contains" in e ? e.contains(t) : e.compareDocumentPosition ? !!(e.compareDocumentPosition(t) & 16) : !1 : !1;
	}
	function Dr(e) {
		e = e != null && e.ownerDocument != null && e.ownerDocument.defaultView != null ? e.ownerDocument.defaultView : window;
		for (var t = Lt(e.document); t instanceof e.HTMLIFrameElement;) {
			try {
				var n = typeof t.contentWindow.location.href == "string";
			} catch {
				n = !1;
			}
			if (n) e = t.contentWindow;
			else break;
			t = Lt(e.document);
		}
		return t;
	}
	function Or(e) {
		var t = e && e.nodeName && e.nodeName.toLowerCase();
		return t && (t === "input" && (e.type === "text" || e.type === "search" || e.type === "tel" || e.type === "url" || e.type === "password") || t === "textarea" || e.contentEditable === "true");
	}
	var kr = ln && "documentMode" in document && 11 >= document.documentMode, Ar = null, jr = null, Mr = null, Nr = !1;
	function Pr(e, t, n) {
		var r = n.window === n ? n.document : n.nodeType === 9 ? n : n.ownerDocument;
		Nr || Ar == null || Ar !== Lt(r) || (r = Ar, "selectionStart" in r && Or(r) ? r = {
			start: r.selectionStart,
			end: r.selectionEnd
		} : (r = (r.ownerDocument && r.ownerDocument.defaultView || window).getSelection(), r = {
			anchorNode: r.anchorNode,
			anchorOffset: r.anchorOffset,
			focusNode: r.focusNode,
			focusOffset: r.focusOffset
		}), Mr && Cr(Mr, r) || (Mr = r, r = Ed(jr, "onSelect"), 0 < r.length && (t = new xn("onSelect", "select", null, t, n), e.push({
			event: t,
			listeners: r
		}), t.target = Ar)));
	}
	function Fr(e, t) {
		var n = {};
		return n[e.toLowerCase()] = t.toLowerCase(), n["Webkit" + e] = "webkit" + t, n["Moz" + e] = "moz" + t, n;
	}
	var Ir = {
		animationend: Fr("Animation", "AnimationEnd"),
		animationiteration: Fr("Animation", "AnimationIteration"),
		animationstart: Fr("Animation", "AnimationStart"),
		transitionrun: Fr("Transition", "TransitionRun"),
		transitionstart: Fr("Transition", "TransitionStart"),
		transitioncancel: Fr("Transition", "TransitionCancel"),
		transitionend: Fr("Transition", "TransitionEnd")
	}, Lr = {}, Rr = {};
	ln && (Rr = document.createElement("div").style, "AnimationEvent" in window || (delete Ir.animationend.animation, delete Ir.animationiteration.animation, delete Ir.animationstart.animation), "TransitionEvent" in window || delete Ir.transitionend.transition);
	function zr(e) {
		if (Lr[e]) return Lr[e];
		if (!Ir[e]) return e;
		var t = Ir[e], n;
		for (n in t) if (t.hasOwnProperty(n) && n in Rr) return Lr[e] = t[n];
		return e;
	}
	var Br = zr("animationend"), Vr = zr("animationiteration"), Hr = zr("animationstart"), Ur = zr("transitionrun"), Wr = zr("transitionstart"), Gr = zr("transitioncancel"), Kr = zr("transitionend"), qr = /* @__PURE__ */ new Map(), Jr = "abort auxClick beforeToggle cancel canPlay canPlayThrough click close contextMenu copy cut drag dragEnd dragEnter dragExit dragLeave dragOver dragStart drop durationChange emptied encrypted ended error gotPointerCapture input invalid keyDown keyPress keyUp load loadedData loadedMetadata loadStart lostPointerCapture mouseDown mouseMove mouseOut mouseOver mouseUp paste pause play playing pointerCancel pointerDown pointerMove pointerOut pointerOver pointerUp progress rateChange reset resize seeked seeking stalled submit suspend timeUpdate touchCancel touchEnd touchStart volumeChange scroll toggle touchMove waiting wheel".split(" ");
	Jr.push("scrollEnd");
	function Yr(e, t) {
		qr.set(e, t), Ct(t, [e]);
	}
	var Xr = typeof reportError == "function" ? reportError : function(e) {
		if (typeof window == "object" && typeof window.ErrorEvent == "function") {
			var t = new window.ErrorEvent("error", {
				bubbles: !0,
				cancelable: !0,
				message: typeof e == "object" && e && typeof e.message == "string" ? String(e.message) : String(e),
				error: e
			});
			if (!window.dispatchEvent(t)) return;
		} else if (typeof process == "object" && typeof process.emit == "function") {
			process.emit("uncaughtException", e);
			return;
		}
		console.error(e);
	}, Zr = [], Qr = 0, $r = 0;
	function ei() {
		for (var e = Qr, t = $r = Qr = 0; t < e;) {
			var n = Zr[t];
			Zr[t++] = null;
			var r = Zr[t];
			Zr[t++] = null;
			var i = Zr[t];
			Zr[t++] = null;
			var a = Zr[t];
			if (Zr[t++] = null, r !== null && i !== null) {
				var o = r.pending;
				o === null ? i.next = i : (i.next = o.next, o.next = i), r.pending = i;
			}
			a !== 0 && ii(n, i, a);
		}
	}
	function ti(e, t, n, r) {
		Zr[Qr++] = e, Zr[Qr++] = t, Zr[Qr++] = n, Zr[Qr++] = r, $r |= r, e.lanes |= r, e = e.alternate, e !== null && (e.lanes |= r);
	}
	function ni(e, t, n, r) {
		return ti(e, t, n, r), ai(e);
	}
	function ri(e, t) {
		return ti(e, null, null, t), ai(e);
	}
	function ii(e, t, n) {
		e.lanes |= n;
		var r = e.alternate;
		r !== null && (r.lanes |= n);
		for (var i = !1, a = e.return; a !== null;) a.childLanes |= n, r = a.alternate, r !== null && (r.childLanes |= n), a.tag === 22 && (e = a.stateNode, e === null || e._visibility & 1 || (i = !0)), e = a, a = a.return;
		return e.tag === 3 ? (a = e.stateNode, i && t !== null && (i = 31 - Re(n), e = a.hiddenUpdates, r = e[i], r === null ? e[i] = [t] : r.push(t), t.lane = n | 536870912), a) : null;
	}
	function ai(e) {
		if (50 < du) throw du = 0, fu = null, Error(a(185));
		for (var t = e.return; t !== null;) e = t, t = e.return;
		return e.tag === 3 ? e.stateNode : null;
	}
	var oi = {};
	function si(e, t, n, r) {
		this.tag = e, this.key = n, this.sibling = this.child = this.return = this.stateNode = this.type = this.elementType = null, this.index = 0, this.refCleanup = this.ref = null, this.pendingProps = t, this.dependencies = this.memoizedState = this.updateQueue = this.memoizedProps = null, this.mode = r, this.subtreeFlags = this.flags = 0, this.deletions = null, this.childLanes = this.lanes = 0, this.alternate = null;
	}
	function ci(e, t, n, r) {
		return new si(e, t, n, r);
	}
	function li(e) {
		return e = e.prototype, !(!e || !e.isReactComponent);
	}
	function ui(e, t) {
		var n = e.alternate;
		return n === null ? (n = ci(e.tag, t, e.key, e.mode), n.elementType = e.elementType, n.type = e.type, n.stateNode = e.stateNode, n.alternate = e, e.alternate = n) : (n.pendingProps = t, n.type = e.type, n.flags = 0, n.subtreeFlags = 0, n.deletions = null), n.flags = e.flags & 65011712, n.childLanes = e.childLanes, n.lanes = e.lanes, n.child = e.child, n.memoizedProps = e.memoizedProps, n.memoizedState = e.memoizedState, n.updateQueue = e.updateQueue, t = e.dependencies, n.dependencies = t === null ? null : {
			lanes: t.lanes,
			firstContext: t.firstContext
		}, n.sibling = e.sibling, n.index = e.index, n.ref = e.ref, n.refCleanup = e.refCleanup, n;
	}
	function di(e, t) {
		e.flags &= 65011714;
		var n = e.alternate;
		return n === null ? (e.childLanes = 0, e.lanes = t, e.child = null, e.subtreeFlags = 0, e.memoizedProps = null, e.memoizedState = null, e.updateQueue = null, e.dependencies = null, e.stateNode = null) : (e.childLanes = n.childLanes, e.lanes = n.lanes, e.child = n.child, e.subtreeFlags = 0, e.deletions = null, e.memoizedProps = n.memoizedProps, e.memoizedState = n.memoizedState, e.updateQueue = n.updateQueue, e.type = n.type, t = n.dependencies, e.dependencies = t === null ? null : {
			lanes: t.lanes,
			firstContext: t.firstContext
		}), e;
	}
	function fi(e, t, n, r, i, o) {
		var s = 0;
		if (r = e, typeof e == "function") li(e) && (s = 1);
		else if (typeof e == "string") s = Uf(e, n, L.current) ? 26 : e === "html" || e === "head" || e === "body" ? 27 : 5;
		else a: switch (e) {
			case k: return e = ci(31, n, t, i), e.elementType = k, e.lanes = o, e;
			case y: return pi(n.children, i, o, t);
			case b:
				s = 8, i |= 24;
				break;
			case x: return e = ci(12, n, t, i | 2), e.elementType = x, e.lanes = o, e;
			case T: return e = ci(13, n, t, i), e.elementType = T, e.lanes = o, e;
			case E: return e = ci(19, n, t, i), e.elementType = E, e.lanes = o, e;
			default:
				if (typeof e == "object" && e) switch (e.$$typeof) {
					case C:
						s = 10;
						break a;
					case S:
						s = 9;
						break a;
					case w:
						s = 11;
						break a;
					case D:
						s = 14;
						break a;
					case O:
						s = 16, r = null;
						break a;
				}
				s = 29, n = Error(a(130, e === null ? "null" : typeof e, "")), r = null;
		}
		return t = ci(s, n, t, i), t.elementType = e, t.type = r, t.lanes = o, t;
	}
	function pi(e, t, n, r) {
		return e = ci(7, e, r, t), e.lanes = n, e;
	}
	function mi(e, t, n) {
		return e = ci(6, e, null, t), e.lanes = n, e;
	}
	function hi(e) {
		var t = ci(18, null, null, 0);
		return t.stateNode = e, t;
	}
	function gi(e, t, n) {
		return t = ci(4, e.children === null ? [] : e.children, e.key, t), t.lanes = n, t.stateNode = {
			containerInfo: e.containerInfo,
			pendingChildren: null,
			implementation: e.implementation
		}, t;
	}
	var _i = /* @__PURE__ */ new WeakMap();
	function vi(e, t) {
		if (typeof e == "object" && e) {
			var n = _i.get(e);
			return n === void 0 ? (t = {
				value: e,
				source: t,
				stack: be(t)
			}, _i.set(e, t), t) : n;
		}
		return {
			value: e,
			source: t,
			stack: be(t)
		};
	}
	var yi = [], bi = 0, xi = null, Si = 0, Ci = [], wi = 0, Ti = null, Ei = 1, Di = "";
	function Oi(e, t) {
		yi[bi++] = Si, yi[bi++] = xi, xi = e, Si = t;
	}
	function ki(e, t, n) {
		Ci[wi++] = Ei, Ci[wi++] = Di, Ci[wi++] = Ti, Ti = e;
		var r = Ei;
		e = Di;
		var i = 32 - Re(r) - 1;
		r &= ~(1 << i), n += 1;
		var a = 32 - Re(t) + i;
		if (30 < a) {
			var o = i - i % 5;
			a = (r & (1 << o) - 1).toString(32), r >>= o, i -= o, Ei = 1 << 32 - Re(t) + i | n << i | r, Di = a + e;
		} else Ei = 1 << a | n << i | r, Di = e;
	}
	function Ai(e) {
		e.return !== null && (Oi(e, 1), ki(e, 1, 0));
	}
	function ji(e) {
		for (; e === xi;) xi = yi[--bi], yi[bi] = null, Si = yi[--bi], yi[bi] = null;
		for (; e === Ti;) Ti = Ci[--wi], Ci[wi] = null, Di = Ci[--wi], Ci[wi] = null, Ei = Ci[--wi], Ci[wi] = null;
	}
	function Mi(e, t) {
		Ci[wi++] = Ei, Ci[wi++] = Di, Ci[wi++] = Ti, Ei = t.id, Di = t.overflow, Ti = e;
	}
	var Ni = null, z = null, B = !1, Pi = null, Fi = !1, Ii = Error(a(519));
	function Li(e) {
		throw Ui(vi(Error(a(418, 1 < arguments.length && arguments[1] !== void 0 && arguments[1] ? "text" : "HTML", "")), e)), Ii;
	}
	function Ri(e) {
		var t = e.stateNode, n = e.type, r = e.memoizedProps;
		switch (t[st] = e, t[ct] = r, n) {
			case "dialog":
				Q("cancel", t), Q("close", t);
				break;
			case "iframe":
			case "object":
			case "embed":
				Q("load", t);
				break;
			case "video":
			case "audio":
				for (n = 0; n < _d.length; n++) Q(_d[n], t);
				break;
			case "source":
				Q("error", t);
				break;
			case "img":
			case "image":
			case "link":
				Q("error", t), Q("load", t);
				break;
			case "details":
				Q("toggle", t);
				break;
			case "input":
				Q("invalid", t), Vt(t, r.value, r.defaultValue, r.checked, r.defaultChecked, r.type, r.name, !0);
				break;
			case "select":
				Q("invalid", t);
				break;
			case "textarea": Q("invalid", t), Gt(t, r.value, r.defaultValue, r.children);
		}
		n = r.children, typeof n != "string" && typeof n != "number" && typeof n != "bigint" || t.textContent === "" + n || !0 === r.suppressHydrationWarning || Md(t.textContent, n) ? (r.popover != null && (Q("beforetoggle", t), Q("toggle", t)), r.onScroll != null && Q("scroll", t), r.onScrollEnd != null && Q("scrollend", t), r.onClick != null && (t.onclick = en), t = !0) : t = !1, t || Li(e, !0);
	}
	function zi(e) {
		for (Ni = e.return; Ni;) switch (Ni.tag) {
			case 5:
			case 31:
			case 13:
				Fi = !1;
				return;
			case 27:
			case 3:
				Fi = !0;
				return;
			default: Ni = Ni.return;
		}
	}
	function Bi(e) {
		if (e !== Ni) return !1;
		if (!B) return zi(e), B = !0, !1;
		var t = e.tag, n;
		if ((n = t !== 3 && t !== 27) && ((n = t === 5) && (n = e.type, n = n === "form" || n === "button" || Ud(e.type, e.memoizedProps)), n = !n), n && z && Li(e), zi(e), t === 13) {
			if (e = e.memoizedState, e = e === null ? null : e.dehydrated, !e) throw Error(a(317));
			z = uf(e);
		} else if (t === 31) {
			if (e = e.memoizedState, e = e === null ? null : e.dehydrated, !e) throw Error(a(317));
			z = uf(e);
		} else t === 27 ? (t = z, Zd(e.type) ? (e = lf, lf = null, z = e) : z = t) : z = Ni ? cf(e.stateNode.nextSibling) : null;
		return !0;
	}
	function Vi() {
		z = Ni = null, B = !1;
	}
	function Hi() {
		var e = Pi;
		return e !== null && (Zl === null ? Zl = e : Zl.push.apply(Zl, e), Pi = null), e;
	}
	function Ui(e) {
		Pi === null ? Pi = [e] : Pi.push(e);
	}
	var Wi = oe(null), Gi = null, Ki = null;
	function qi(e, t, n) {
		I(Wi, t._currentValue), t._currentValue = n;
	}
	function Ji(e) {
		e._currentValue = Wi.current, F(Wi);
	}
	function Yi(e, t, n) {
		for (; e !== null;) {
			var r = e.alternate;
			if ((e.childLanes & t) === t ? r !== null && (r.childLanes & t) !== t && (r.childLanes |= t) : (e.childLanes |= t, r !== null && (r.childLanes |= t)), e === n) break;
			e = e.return;
		}
	}
	function Xi(e, t, n, r) {
		var i = e.child;
		for (i !== null && (i.return = e); i !== null;) {
			var o = i.dependencies;
			if (o !== null) {
				var s = i.child;
				o = o.firstContext;
				a: for (; o !== null;) {
					var c = o;
					o = i;
					for (var l = 0; l < t.length; l++) if (c.context === t[l]) {
						o.lanes |= n, c = o.alternate, c !== null && (c.lanes |= n), Yi(o.return, n, e), r || (s = null);
						break a;
					}
					o = c.next;
				}
			} else if (i.tag === 18) {
				if (s = i.return, s === null) throw Error(a(341));
				s.lanes |= n, o = s.alternate, o !== null && (o.lanes |= n), Yi(s, n, e), s = null;
			} else s = i.child;
			if (s !== null) s.return = i;
			else for (s = i; s !== null;) {
				if (s === e) {
					s = null;
					break;
				}
				if (i = s.sibling, i !== null) {
					i.return = s.return, s = i;
					break;
				}
				s = s.return;
			}
			i = s;
		}
	}
	function Zi(e, t, n, r) {
		e = null;
		for (var i = t, o = !1; i !== null;) {
			if (!o) {
				if (i.flags & 524288) o = !0;
				else if (i.flags & 262144) break;
			}
			if (i.tag === 10) {
				var s = i.alternate;
				if (s === null) throw Error(a(387));
				if (s = s.memoizedProps, s !== null) {
					var c = i.type;
					Sr(i.pendingProps.value, s.value) || (e === null ? e = [c] : e.push(c));
				}
			} else if (i === le.current) {
				if (s = i.alternate, s === null) throw Error(a(387));
				s.memoizedState.memoizedState !== i.memoizedState.memoizedState && (e === null ? e = [Qf] : e.push(Qf));
			}
			i = i.return;
		}
		e !== null && Xi(t, e, n, r), t.flags |= 262144;
	}
	function Qi(e) {
		for (e = e.firstContext; e !== null;) {
			if (!Sr(e.context._currentValue, e.memoizedValue)) return !0;
			e = e.next;
		}
		return !1;
	}
	function $i(e) {
		Gi = e, Ki = null, e = e.dependencies, e !== null && (e.firstContext = null);
	}
	function ea(e) {
		return na(Gi, e);
	}
	function ta(e, t) {
		return Gi === null && $i(e), na(e, t);
	}
	function na(e, t) {
		var n = t._currentValue;
		if (t = {
			context: t,
			memoizedValue: n,
			next: null
		}, Ki === null) {
			if (e === null) throw Error(a(308));
			Ki = t, e.dependencies = {
				lanes: 0,
				firstContext: t
			}, e.flags |= 524288;
		} else Ki = Ki.next = t;
		return n;
	}
	var ra = typeof AbortController < "u" ? AbortController : function() {
		var e = [], t = this.signal = {
			aborted: !1,
			addEventListener: function(t, n) {
				e.push(n);
			}
		};
		this.abort = function() {
			t.aborted = !0, e.forEach(function(e) {
				return e();
			});
		};
	}, ia = t.unstable_scheduleCallback, aa = t.unstable_NormalPriority, oa = {
		$$typeof: C,
		Consumer: null,
		Provider: null,
		_currentValue: null,
		_currentValue2: null,
		_threadCount: 0
	};
	function sa() {
		return {
			controller: new ra(),
			data: /* @__PURE__ */ new Map(),
			refCount: 0
		};
	}
	function ca(e) {
		e.refCount--, e.refCount === 0 && ia(aa, function() {
			e.controller.abort();
		});
	}
	var la = null, ua = 0, da = 0, fa = null;
	function pa(e, t) {
		if (la === null) {
			var n = la = [];
			ua = 0, da = dd(), fa = {
				status: "pending",
				value: void 0,
				then: function(e) {
					n.push(e);
				}
			};
		}
		return ua++, t.then(ma, ma), t;
	}
	function ma() {
		if (--ua === 0 && la !== null) {
			fa !== null && (fa.status = "fulfilled");
			var e = la;
			la = null, da = 0, fa = null;
			for (var t = 0; t < e.length; t++) (0, e[t])();
		}
	}
	function ha(e, t) {
		var n = [], r = {
			status: "pending",
			value: null,
			reason: null,
			then: function(e) {
				n.push(e);
			}
		};
		return e.then(function() {
			r.status = "fulfilled", r.value = t;
			for (var e = 0; e < n.length; e++) (0, n[e])(t);
		}, function(e) {
			for (r.status = "rejected", r.reason = e, e = 0; e < n.length; e++) (0, n[e])(void 0);
		}), r;
	}
	var ga = N.S;
	N.S = function(e, t) {
		eu = Ee(), typeof t == "object" && t && typeof t.then == "function" && pa(e, t), ga !== null && ga(e, t);
	};
	var V = oe(null);
	function _a() {
		var e = V.current;
		return e === null ? q.pooledCache : e;
	}
	function va(e, t) {
		t === null ? I(V, V.current) : I(V, t.pool);
	}
	function H() {
		var e = _a();
		return e === null ? null : {
			parent: oa._currentValue,
			pool: e
		};
	}
	var U = Error(a(460)), ya = Error(a(474)), ba = Error(a(542)), xa = { then: function() {} };
	function Sa(e) {
		return e = e.status, e === "fulfilled" || e === "rejected";
	}
	function Ca(e, t, n) {
		switch (n = e[n], n === void 0 ? e.push(t) : n !== t && (t.then(en, en), t = n), t.status) {
			case "fulfilled": return t.value;
			case "rejected": throw e = t.reason, Da(e), e;
			default:
				if (typeof t.status == "string") t.then(en, en);
				else {
					if (e = q, e !== null && 100 < e.shellSuspendCounter) throw Error(a(482));
					e = t, e.status = "pending", e.then(function(e) {
						if (t.status === "pending") {
							var n = t;
							n.status = "fulfilled", n.value = e;
						}
					}, function(e) {
						if (t.status === "pending") {
							var n = t;
							n.status = "rejected", n.reason = e;
						}
					});
				}
				switch (t.status) {
					case "fulfilled": return t.value;
					case "rejected": throw e = t.reason, Da(e), e;
				}
				throw Ta = t, U;
		}
	}
	function wa(e) {
		try {
			var t = e._init;
			return t(e._payload);
		} catch (e) {
			throw typeof e == "object" && e && typeof e.then == "function" ? (Ta = e, U) : e;
		}
	}
	var Ta = null;
	function Ea() {
		if (Ta === null) throw Error(a(459));
		var e = Ta;
		return Ta = null, e;
	}
	function Da(e) {
		if (e === U || e === ba) throw Error(a(483));
	}
	var Oa = null, ka = 0;
	function Aa(e) {
		var t = ka;
		return ka += 1, Oa === null && (Oa = []), Ca(Oa, e, t);
	}
	function ja(e, t) {
		t = t.props.ref, e.ref = t === void 0 ? null : t;
	}
	function Ma(e, t) {
		throw t.$$typeof === g ? Error(a(525)) : (e = Object.prototype.toString.call(t), Error(a(31, e === "[object Object]" ? "object with keys {" + Object.keys(t).join(", ") + "}" : e)));
	}
	function Na(e) {
		function t(t, n) {
			if (e) {
				var r = t.deletions;
				r === null ? (t.deletions = [n], t.flags |= 16) : r.push(n);
			}
		}
		function n(n, r) {
			if (!e) return null;
			for (; r !== null;) t(n, r), r = r.sibling;
			return null;
		}
		function r(e) {
			for (var t = /* @__PURE__ */ new Map(); e !== null;) e.key === null ? t.set(e.index, e) : t.set(e.key, e), e = e.sibling;
			return t;
		}
		function i(e, t) {
			return e = ui(e, t), e.index = 0, e.sibling = null, e;
		}
		function o(t, n, r) {
			return t.index = r, e ? (r = t.alternate, r === null ? (t.flags |= 67108866, n) : (r = r.index, r < n ? (t.flags |= 67108866, n) : r)) : (t.flags |= 1048576, n);
		}
		function s(t) {
			return e && t.alternate === null && (t.flags |= 67108866), t;
		}
		function c(e, t, n, r) {
			return t === null || t.tag !== 6 ? (t = mi(n, e.mode, r), t.return = e, t) : (t = i(t, n), t.return = e, t);
		}
		function l(e, t, n, r) {
			var a = n.type;
			return a === y ? d(e, t, n.props.children, r, n.key) : t !== null && (t.elementType === a || typeof a == "object" && a && a.$$typeof === O && wa(a) === t.type) ? (t = i(t, n.props), ja(t, n), t.return = e, t) : (t = fi(n.type, n.key, n.props, null, e.mode, r), ja(t, n), t.return = e, t);
		}
		function u(e, t, n, r) {
			return t === null || t.tag !== 4 || t.stateNode.containerInfo !== n.containerInfo || t.stateNode.implementation !== n.implementation ? (t = gi(n, e.mode, r), t.return = e, t) : (t = i(t, n.children || []), t.return = e, t);
		}
		function d(e, t, n, r, a) {
			return t === null || t.tag !== 7 ? (t = pi(n, e.mode, r, a), t.return = e, t) : (t = i(t, n), t.return = e, t);
		}
		function f(e, t, n) {
			if (typeof t == "string" && t !== "" || typeof t == "number" || typeof t == "bigint") return t = mi("" + t, e.mode, n), t.return = e, t;
			if (typeof t == "object" && t) {
				switch (t.$$typeof) {
					case _: return n = fi(t.type, t.key, t.props, null, e.mode, n), ja(n, t), n.return = e, n;
					case v: return t = gi(t, e.mode, n), t.return = e, t;
					case O: return t = wa(t), f(e, t, n);
				}
				if (ne(t) || M(t)) return t = pi(t, e.mode, n, null), t.return = e, t;
				if (typeof t.then == "function") return f(e, Aa(t), n);
				if (t.$$typeof === C) return f(e, ta(e, t), n);
				Ma(e, t);
			}
			return null;
		}
		function p(e, t, n, r) {
			var i = t === null ? null : t.key;
			if (typeof n == "string" && n !== "" || typeof n == "number" || typeof n == "bigint") return i === null ? c(e, t, "" + n, r) : null;
			if (typeof n == "object" && n) {
				switch (n.$$typeof) {
					case _: return n.key === i ? l(e, t, n, r) : null;
					case v: return n.key === i ? u(e, t, n, r) : null;
					case O: return n = wa(n), p(e, t, n, r);
				}
				if (ne(n) || M(n)) return i === null ? d(e, t, n, r, null) : null;
				if (typeof n.then == "function") return p(e, t, Aa(n), r);
				if (n.$$typeof === C) return p(e, t, ta(e, n), r);
				Ma(e, n);
			}
			return null;
		}
		function m(e, t, n, r, i) {
			if (typeof r == "string" && r !== "" || typeof r == "number" || typeof r == "bigint") return e = e.get(n) || null, c(t, e, "" + r, i);
			if (typeof r == "object" && r) {
				switch (r.$$typeof) {
					case _: return e = e.get(r.key === null ? n : r.key) || null, l(t, e, r, i);
					case v: return e = e.get(r.key === null ? n : r.key) || null, u(t, e, r, i);
					case O: return r = wa(r), m(e, t, n, r, i);
				}
				if (ne(r) || M(r)) return e = e.get(n) || null, d(t, e, r, i, null);
				if (typeof r.then == "function") return m(e, t, n, Aa(r), i);
				if (r.$$typeof === C) return m(e, t, n, ta(t, r), i);
				Ma(t, r);
			}
			return null;
		}
		function h(i, a, s, c) {
			for (var l = null, u = null, d = a, h = a = 0, g = null; d !== null && h < s.length; h++) {
				d.index > h ? (g = d, d = null) : g = d.sibling;
				var _ = p(i, d, s[h], c);
				if (_ === null) {
					d === null && (d = g);
					break;
				}
				e && d && _.alternate === null && t(i, d), a = o(_, a, h), u === null ? l = _ : u.sibling = _, u = _, d = g;
			}
			if (h === s.length) return n(i, d), B && Oi(i, h), l;
			if (d === null) {
				for (; h < s.length; h++) d = f(i, s[h], c), d !== null && (a = o(d, a, h), u === null ? l = d : u.sibling = d, u = d);
				return B && Oi(i, h), l;
			}
			for (d = r(d); h < s.length; h++) g = m(d, i, h, s[h], c), g !== null && (e && g.alternate !== null && d.delete(g.key === null ? h : g.key), a = o(g, a, h), u === null ? l = g : u.sibling = g, u = g);
			return e && d.forEach(function(e) {
				return t(i, e);
			}), B && Oi(i, h), l;
		}
		function g(i, s, c, l) {
			if (c == null) throw Error(a(151));
			for (var u = null, d = null, h = s, g = s = 0, _ = null, v = c.next(); h !== null && !v.done; g++, v = c.next()) {
				h.index > g ? (_ = h, h = null) : _ = h.sibling;
				var y = p(i, h, v.value, l);
				if (y === null) {
					h === null && (h = _);
					break;
				}
				e && h && y.alternate === null && t(i, h), s = o(y, s, g), d === null ? u = y : d.sibling = y, d = y, h = _;
			}
			if (v.done) return n(i, h), B && Oi(i, g), u;
			if (h === null) {
				for (; !v.done; g++, v = c.next()) v = f(i, v.value, l), v !== null && (s = o(v, s, g), d === null ? u = v : d.sibling = v, d = v);
				return B && Oi(i, g), u;
			}
			for (h = r(h); !v.done; g++, v = c.next()) v = m(h, i, g, v.value, l), v !== null && (e && v.alternate !== null && h.delete(v.key === null ? g : v.key), s = o(v, s, g), d === null ? u = v : d.sibling = v, d = v);
			return e && h.forEach(function(e) {
				return t(i, e);
			}), B && Oi(i, g), u;
		}
		function b(e, r, o, c) {
			if (typeof o == "object" && o && o.type === y && o.key === null && (o = o.props.children), typeof o == "object" && o) {
				switch (o.$$typeof) {
					case _:
						a: {
							for (var l = o.key; r !== null;) {
								if (r.key === l) {
									if (l = o.type, l === y) {
										if (r.tag === 7) {
											n(e, r.sibling), c = i(r, o.props.children), c.return = e, e = c;
											break a;
										}
									} else if (r.elementType === l || typeof l == "object" && l && l.$$typeof === O && wa(l) === r.type) {
										n(e, r.sibling), c = i(r, o.props), ja(c, o), c.return = e, e = c;
										break a;
									}
									n(e, r);
									break;
								}
								t(e, r), r = r.sibling;
							}
							o.type === y ? (c = pi(o.props.children, e.mode, c, o.key), c.return = e, e = c) : (c = fi(o.type, o.key, o.props, null, e.mode, c), ja(c, o), c.return = e, e = c);
						}
						return s(e);
					case v:
						a: {
							for (l = o.key; r !== null;) {
								if (r.key === l) {
									if (r.tag === 4 && r.stateNode.containerInfo === o.containerInfo && r.stateNode.implementation === o.implementation) {
										n(e, r.sibling), c = i(r, o.children || []), c.return = e, e = c;
										break a;
									}
									n(e, r);
									break;
								}
								t(e, r), r = r.sibling;
							}
							c = gi(o, e.mode, c), c.return = e, e = c;
						}
						return s(e);
					case O: return o = wa(o), b(e, r, o, c);
				}
				if (ne(o)) return h(e, r, o, c);
				if (M(o)) {
					if (l = M(o), typeof l != "function") throw Error(a(150));
					return o = l.call(o), g(e, r, o, c);
				}
				if (typeof o.then == "function") return b(e, r, Aa(o), c);
				if (o.$$typeof === C) return b(e, r, ta(e, o), c);
				Ma(e, o);
			}
			return typeof o == "string" && o !== "" || typeof o == "number" || typeof o == "bigint" ? (o = "" + o, r !== null && r.tag === 6 ? (n(e, r.sibling), c = i(r, o), c.return = e, e = c) : (n(e, r), c = mi(o, e.mode, c), c.return = e, e = c), s(e)) : n(e, r);
		}
		return function(e, t, n, r) {
			try {
				ka = 0;
				var i = b(e, t, n, r);
				return Oa = null, i;
			} catch (t) {
				if (t === U || t === ba) throw t;
				var a = ci(29, t, null, e.mode);
				return a.lanes = r, a.return = e, a;
			}
		};
	}
	var Pa = Na(!0), Fa = Na(!1), Ia = !1;
	function La(e) {
		e.updateQueue = {
			baseState: e.memoizedState,
			firstBaseUpdate: null,
			lastBaseUpdate: null,
			shared: {
				pending: null,
				lanes: 0,
				hiddenCallbacks: null
			},
			callbacks: null
		};
	}
	function Ra(e, t) {
		e = e.updateQueue, t.updateQueue === e && (t.updateQueue = {
			baseState: e.baseState,
			firstBaseUpdate: e.firstBaseUpdate,
			lastBaseUpdate: e.lastBaseUpdate,
			shared: e.shared,
			callbacks: null
		});
	}
	function za(e) {
		return {
			lane: e,
			tag: 0,
			payload: null,
			callback: null,
			next: null
		};
	}
	function Ba(e, t, n) {
		var r = e.updateQueue;
		if (r === null) return null;
		if (r = r.shared, K & 2) {
			var i = r.pending;
			return i === null ? t.next = t : (t.next = i.next, i.next = t), r.pending = t, t = ai(e), ii(e, null, n), t;
		}
		return ti(e, r, t, n), ai(e);
	}
	function Va(e, t, n) {
		if (t = t.updateQueue, t !== null && (t = t.shared, n & 4194048)) {
			var r = t.lanes;
			r &= e.pendingLanes, n |= r, t.lanes = n, et(e, n);
		}
	}
	function Ha(e, t) {
		var n = e.updateQueue, r = e.alternate;
		if (r !== null && (r = r.updateQueue, n === r)) {
			var i = null, a = null;
			if (n = n.firstBaseUpdate, n !== null) {
				do {
					var o = {
						lane: n.lane,
						tag: n.tag,
						payload: n.payload,
						callback: null,
						next: null
					};
					a === null ? i = a = o : a = a.next = o, n = n.next;
				} while (n !== null);
				a === null ? i = a = t : a = a.next = t;
			} else i = a = t;
			n = {
				baseState: r.baseState,
				firstBaseUpdate: i,
				lastBaseUpdate: a,
				shared: r.shared,
				callbacks: r.callbacks
			}, e.updateQueue = n;
			return;
		}
		e = n.lastBaseUpdate, e === null ? n.firstBaseUpdate = t : e.next = t, n.lastBaseUpdate = t;
	}
	var Ua = !1;
	function Wa() {
		if (Ua) {
			var e = fa;
			if (e !== null) throw e;
		}
	}
	function Ga(e, t, n, r) {
		Ua = !1;
		var i = e.updateQueue;
		Ia = !1;
		var a = i.firstBaseUpdate, o = i.lastBaseUpdate, s = i.shared.pending;
		if (s !== null) {
			i.shared.pending = null;
			var c = s, l = c.next;
			c.next = null, o === null ? a = l : o.next = l, o = c;
			var u = e.alternate;
			u !== null && (u = u.updateQueue, s = u.lastBaseUpdate, s !== o && (s === null ? u.firstBaseUpdate = l : s.next = l, u.lastBaseUpdate = c));
		}
		if (a !== null) {
			var d = i.baseState;
			o = 0, u = l = c = null, s = a;
			do {
				var f = s.lane & -536870913, p = f !== s.lane;
				if (p ? (Y & f) === f : (r & f) === f) {
					f !== 0 && f === da && (Ua = !0), u !== null && (u = u.next = {
						lane: 0,
						tag: s.tag,
						payload: s.payload,
						callback: null,
						next: null
					});
					a: {
						var m = e, g = s;
						f = t;
						var _ = n;
						switch (g.tag) {
							case 1:
								if (m = g.payload, typeof m == "function") {
									d = m.call(_, d, f);
									break a;
								}
								d = m;
								break a;
							case 3: m.flags = m.flags & -65537 | 128;
							case 0:
								if (m = g.payload, f = typeof m == "function" ? m.call(_, d, f) : m, f == null) break a;
								d = h({}, d, f);
								break a;
							case 2: Ia = !0;
						}
					}
					f = s.callback, f !== null && (e.flags |= 64, p && (e.flags |= 8192), p = i.callbacks, p === null ? i.callbacks = [f] : p.push(f));
				} else p = {
					lane: f,
					tag: s.tag,
					payload: s.payload,
					callback: s.callback,
					next: null
				}, u === null ? (l = u = p, c = d) : u = u.next = p, o |= f;
				if (s = s.next, s === null) {
					if (s = i.shared.pending, s === null) break;
					p = s, s = p.next, p.next = null, i.lastBaseUpdate = p, i.shared.pending = null;
				}
			} while (1);
			u === null && (c = d), i.baseState = c, i.firstBaseUpdate = l, i.lastBaseUpdate = u, a === null && (i.shared.lanes = 0), Gl |= o, e.lanes = o, e.memoizedState = d;
		}
	}
	function Ka(e, t) {
		if (typeof e != "function") throw Error(a(191, e));
		e.call(t);
	}
	function qa(e, t) {
		var n = e.callbacks;
		if (n !== null) for (e.callbacks = null, e = 0; e < n.length; e++) Ka(n[e], t);
	}
	var Ja = oe(null), Ya = oe(0);
	function Xa(e, t) {
		e = Ul, I(Ya, e), I(Ja, t), Ul = e | t.baseLanes;
	}
	function Za() {
		I(Ya, Ul), I(Ja, Ja.current);
	}
	function Qa() {
		Ul = Ya.current, F(Ja), F(Ya);
	}
	var $a = oe(null), eo = null;
	function to(e) {
		var t = e.alternate;
		I(oo, oo.current & 1), I($a, e), eo === null && (t === null || Ja.current !== null || t.memoizedState !== null) && (eo = e);
	}
	function no(e) {
		I(oo, oo.current), I($a, e), eo === null && (eo = e);
	}
	function ro(e) {
		e.tag === 22 ? (I(oo, oo.current), I($a, e), eo === null && (eo = e)) : io(e);
	}
	function io() {
		I(oo, oo.current), I($a, $a.current);
	}
	function ao(e) {
		F($a), eo === e && (eo = null), F(oo);
	}
	var oo = oe(0);
	function so(e) {
		for (var t = e; t !== null;) {
			if (t.tag === 13) {
				var n = t.memoizedState;
				if (n !== null && (n = n.dehydrated, n === null || af(n) || of(n))) return t;
			} else if (t.tag === 19 && (t.memoizedProps.revealOrder === "forwards" || t.memoizedProps.revealOrder === "backwards" || t.memoizedProps.revealOrder === "unstable_legacy-backwards" || t.memoizedProps.revealOrder === "together")) {
				if (t.flags & 128) return t;
			} else if (t.child !== null) {
				t.child.return = t, t = t.child;
				continue;
			}
			if (t === e) break;
			for (; t.sibling === null;) {
				if (t.return === null || t.return === e) return null;
				t = t.return;
			}
			t.sibling.return = t.return, t = t.sibling;
		}
		return null;
	}
	var co = 0, W = null, G = null, lo = null, uo = !1, fo = !1, po = !1, mo = 0, ho = 0, go = null, _o = 0;
	function vo() {
		throw Error(a(321));
	}
	function yo(e, t) {
		if (t === null) return !1;
		for (var n = 0; n < t.length && n < e.length; n++) if (!Sr(e[n], t[n])) return !1;
		return !0;
	}
	function bo(e, t, n, r, i, a) {
		return co = a, W = t, t.memoizedState = null, t.updateQueue = null, t.lanes = 0, N.H = e === null || e.memoizedState === null ? Ls : Rs, po = !1, a = n(r, i), po = !1, fo && (a = So(t, n, r, i)), xo(e), a;
	}
	function xo(e) {
		N.H = Is;
		var t = G !== null && G.next !== null;
		if (co = 0, lo = G = W = null, uo = !1, ho = 0, go = null, t) throw Error(a(300));
		e === null || tc || (e = e.dependencies, e !== null && Qi(e) && (tc = !0));
	}
	function So(e, t, n, r) {
		W = e;
		var i = 0;
		do {
			if (fo && (go = null), ho = 0, fo = !1, 25 <= i) throw Error(a(301));
			if (i += 1, lo = G = null, e.updateQueue != null) {
				var o = e.updateQueue;
				o.lastEffect = null, o.events = null, o.stores = null, o.memoCache != null && (o.memoCache.index = 0);
			}
			N.H = zs, o = t(n, r);
		} while (fo);
		return o;
	}
	function Co() {
		var e = N.H, t = e.useState()[0];
		return t = typeof t.then == "function" ? Ao(t) : t, e = e.useState()[0], (G === null ? null : G.memoizedState) !== e && (W.flags |= 1024), t;
	}
	function wo() {
		var e = mo !== 0;
		return mo = 0, e;
	}
	function To(e, t, n) {
		t.updateQueue = e.updateQueue, t.flags &= -2053, e.lanes &= ~n;
	}
	function Eo(e) {
		if (uo) {
			for (e = e.memoizedState; e !== null;) {
				var t = e.queue;
				t !== null && (t.pending = null), e = e.next;
			}
			uo = !1;
		}
		co = 0, lo = G = W = null, fo = !1, ho = mo = 0, go = null;
	}
	function Do() {
		var e = {
			memoizedState: null,
			baseState: null,
			baseQueue: null,
			queue: null,
			next: null
		};
		return lo === null ? W.memoizedState = lo = e : lo = lo.next = e, lo;
	}
	function Oo() {
		if (G === null) {
			var e = W.alternate;
			e = e === null ? null : e.memoizedState;
		} else e = G.next;
		var t = lo === null ? W.memoizedState : lo.next;
		if (t !== null) lo = t, G = e;
		else {
			if (e === null) throw W.alternate === null ? Error(a(467)) : Error(a(310));
			G = e, e = {
				memoizedState: G.memoizedState,
				baseState: G.baseState,
				baseQueue: G.baseQueue,
				queue: G.queue,
				next: null
			}, lo === null ? W.memoizedState = lo = e : lo = lo.next = e;
		}
		return lo;
	}
	function ko() {
		return {
			lastEffect: null,
			events: null,
			stores: null,
			memoCache: null
		};
	}
	function Ao(e) {
		var t = ho;
		return ho += 1, go === null && (go = []), e = Ca(go, e, t), t = W, (lo === null ? t.memoizedState : lo.next) === null && (t = t.alternate, N.H = t === null || t.memoizedState === null ? Ls : Rs), e;
	}
	function jo(e) {
		if (typeof e == "object" && e) {
			if (typeof e.then == "function") return Ao(e);
			if (e.$$typeof === C) return ea(e);
		}
		throw Error(a(438, String(e)));
	}
	function Mo(e) {
		var t = null, n = W.updateQueue;
		if (n !== null && (t = n.memoCache), t == null) {
			var r = W.alternate;
			r !== null && (r = r.updateQueue, r !== null && (r = r.memoCache, r != null && (t = {
				data: r.data.map(function(e) {
					return e.slice();
				}),
				index: 0
			})));
		}
		if (t ??= {
			data: [],
			index: 0
		}, n === null && (n = ko(), W.updateQueue = n), n.memoCache = t, n = t.data[t.index], n === void 0) for (n = t.data[t.index] = Array(e), r = 0; r < e; r++) n[r] = A;
		return t.index++, n;
	}
	function No(e, t) {
		return typeof t == "function" ? t(e) : t;
	}
	function Po(e) {
		return Fo(Oo(), G, e);
	}
	function Fo(e, t, n) {
		var r = e.queue;
		if (r === null) throw Error(a(311));
		r.lastRenderedReducer = n;
		var i = e.baseQueue, o = r.pending;
		if (o !== null) {
			if (i !== null) {
				var s = i.next;
				i.next = o.next, o.next = s;
			}
			t.baseQueue = i = o, r.pending = null;
		}
		if (o = e.baseState, i === null) e.memoizedState = o;
		else {
			t = i.next;
			var c = s = null, l = null, u = t, d = !1;
			do {
				var f = u.lane & -536870913;
				if (f === u.lane ? (co & f) === f : (Y & f) === f) {
					var p = u.revertLane;
					if (p === 0) l !== null && (l = l.next = {
						lane: 0,
						revertLane: 0,
						gesture: null,
						action: u.action,
						hasEagerState: u.hasEagerState,
						eagerState: u.eagerState,
						next: null
					}), f === da && (d = !0);
					else if ((co & p) === p) {
						u = u.next, p === da && (d = !0);
						continue;
					} else f = {
						lane: 0,
						revertLane: u.revertLane,
						gesture: null,
						action: u.action,
						hasEagerState: u.hasEagerState,
						eagerState: u.eagerState,
						next: null
					}, l === null ? (c = l = f, s = o) : l = l.next = f, W.lanes |= p, Gl |= p;
					f = u.action, po && n(o, f), o = u.hasEagerState ? u.eagerState : n(o, f);
				} else p = {
					lane: f,
					revertLane: u.revertLane,
					gesture: u.gesture,
					action: u.action,
					hasEagerState: u.hasEagerState,
					eagerState: u.eagerState,
					next: null
				}, l === null ? (c = l = p, s = o) : l = l.next = p, W.lanes |= f, Gl |= f;
				u = u.next;
			} while (u !== null && u !== t);
			if (l === null ? s = o : l.next = c, !Sr(o, e.memoizedState) && (tc = !0, d && (n = fa, n !== null))) throw n;
			e.memoizedState = o, e.baseState = s, e.baseQueue = l, r.lastRenderedState = o;
		}
		return i === null && (r.lanes = 0), [e.memoizedState, r.dispatch];
	}
	function Io(e) {
		var t = Oo(), n = t.queue;
		if (n === null) throw Error(a(311));
		n.lastRenderedReducer = e;
		var r = n.dispatch, i = n.pending, o = t.memoizedState;
		if (i !== null) {
			n.pending = null;
			var s = i = i.next;
			do
				o = e(o, s.action), s = s.next;
			while (s !== i);
			Sr(o, t.memoizedState) || (tc = !0), t.memoizedState = o, t.baseQueue === null && (t.baseState = o), n.lastRenderedState = o;
		}
		return [o, r];
	}
	function Lo(e, t, n) {
		var r = W, i = Oo(), o = B;
		if (o) {
			if (n === void 0) throw Error(a(407));
			n = n();
		} else n = t();
		var s = !Sr((G || i).memoizedState, n);
		if (s && (i.memoizedState = n, tc = !0), i = i.queue, cs(Bo.bind(null, r, i, e), [e]), i.getSnapshot !== t || s || lo !== null && lo.memoizedState.tag & 1) {
			if (r.flags |= 2048, rs(9, { destroy: void 0 }, zo.bind(null, r, i, n, t), null), q === null) throw Error(a(349));
			o || co & 127 || Ro(r, t, n);
		}
		return n;
	}
	function Ro(e, t, n) {
		e.flags |= 16384, e = {
			getSnapshot: t,
			value: n
		}, t = W.updateQueue, t === null ? (t = ko(), W.updateQueue = t, t.stores = [e]) : (n = t.stores, n === null ? t.stores = [e] : n.push(e));
	}
	function zo(e, t, n, r) {
		t.value = n, t.getSnapshot = r, Vo(t) && Ho(e);
	}
	function Bo(e, t, n) {
		return n(function() {
			Vo(t) && Ho(e);
		});
	}
	function Vo(e) {
		var t = e.getSnapshot;
		e = e.value;
		try {
			var n = t();
			return !Sr(e, n);
		} catch {
			return !0;
		}
	}
	function Ho(e) {
		var t = ri(e, 2);
		t !== null && hu(t, e, 2);
	}
	function Uo(e) {
		var t = Do();
		if (typeof e == "function") {
			var n = e;
			if (e = n(), po) {
				Le(!0);
				try {
					n();
				} finally {
					Le(!1);
				}
			}
		}
		return t.memoizedState = t.baseState = e, t.queue = {
			pending: null,
			lanes: 0,
			dispatch: null,
			lastRenderedReducer: No,
			lastRenderedState: e
		}, t;
	}
	function Wo(e, t, n, r) {
		return e.baseState = n, Fo(e, G, typeof r == "function" ? r : No);
	}
	function Go(e, t, n, r, i) {
		if (Ns(e)) throw Error(a(485));
		if (e = t.action, e !== null) {
			var o = {
				payload: i,
				action: e,
				next: null,
				isTransition: !0,
				status: "pending",
				value: null,
				reason: null,
				listeners: [],
				then: function(e) {
					o.listeners.push(e);
				}
			};
			N.T === null ? o.isTransition = !1 : n(!0), r(o), n = t.pending, n === null ? (o.next = t.pending = o, Ko(t, o)) : (o.next = n.next, t.pending = n.next = o);
		}
	}
	function Ko(e, t) {
		var n = t.action, r = t.payload, i = e.state;
		if (t.isTransition) {
			var a = N.T, o = {};
			N.T = o;
			try {
				var s = n(i, r), c = N.S;
				c !== null && c(o, s), qo(e, t, s);
			} catch (n) {
				Yo(e, t, n);
			} finally {
				a !== null && o.types !== null && (a.types = o.types), N.T = a;
			}
		} else try {
			a = n(i, r), qo(e, t, a);
		} catch (n) {
			Yo(e, t, n);
		}
	}
	function qo(e, t, n) {
		typeof n == "object" && n && typeof n.then == "function" ? n.then(function(n) {
			Jo(e, t, n);
		}, function(n) {
			return Yo(e, t, n);
		}) : Jo(e, t, n);
	}
	function Jo(e, t, n) {
		t.status = "fulfilled", t.value = n, Xo(t), e.state = n, t = e.pending, t !== null && (n = t.next, n === t ? e.pending = null : (n = n.next, t.next = n, Ko(e, n)));
	}
	function Yo(e, t, n) {
		var r = e.pending;
		if (e.pending = null, r !== null) {
			r = r.next;
			do
				t.status = "rejected", t.reason = n, Xo(t), t = t.next;
			while (t !== r);
		}
		e.action = null;
	}
	function Xo(e) {
		e = e.listeners;
		for (var t = 0; t < e.length; t++) (0, e[t])();
	}
	function Zo(e, t) {
		return t;
	}
	function Qo(e, t) {
		if (B) {
			var n = q.formState;
			if (n !== null) {
				a: {
					var r = W;
					if (B) {
						if (z) {
							b: {
								for (var i = z, a = Fi; i.nodeType !== 8;) {
									if (!a) {
										i = null;
										break b;
									}
									if (i = cf(i.nextSibling), i === null) {
										i = null;
										break b;
									}
								}
								a = i.data, i = a === "F!" || a === "F" ? i : null;
							}
							if (i) {
								z = cf(i.nextSibling), r = i.data === "F!";
								break a;
							}
						}
						Li(r);
					}
					r = !1;
				}
				r && (t = n[0]);
			}
		}
		return n = Do(), n.memoizedState = n.baseState = t, r = {
			pending: null,
			lanes: 0,
			dispatch: null,
			lastRenderedReducer: Zo,
			lastRenderedState: t
		}, n.queue = r, n = As.bind(null, W, r), r.dispatch = n, r = Uo(!1), a = Ms.bind(null, W, !1, r.queue), r = Do(), i = {
			state: t,
			dispatch: null,
			action: e,
			pending: null
		}, r.queue = i, n = Go.bind(null, W, i, a, n), i.dispatch = n, r.memoizedState = e, [
			t,
			n,
			!1
		];
	}
	function $o(e) {
		return es(Oo(), G, e);
	}
	function es(e, t, n) {
		if (t = Fo(e, t, Zo)[0], e = Po(No)[0], typeof t == "object" && t && typeof t.then == "function") try {
			var r = Ao(t);
		} catch (e) {
			throw e === U ? ba : e;
		}
		else r = t;
		t = Oo();
		var i = t.queue, a = i.dispatch;
		return n !== t.memoizedState && (W.flags |= 2048, rs(9, { destroy: void 0 }, ts.bind(null, i, n), null)), [
			r,
			a,
			e
		];
	}
	function ts(e, t) {
		e.action = t;
	}
	function ns(e) {
		var t = Oo(), n = G;
		if (n !== null) return es(t, n, e);
		Oo(), t = t.memoizedState, n = Oo();
		var r = n.queue.dispatch;
		return n.memoizedState = e, [
			t,
			r,
			!1
		];
	}
	function rs(e, t, n, r) {
		return e = {
			tag: e,
			create: n,
			deps: r,
			inst: t,
			next: null
		}, t = W.updateQueue, t === null && (t = ko(), W.updateQueue = t), n = t.lastEffect, n === null ? t.lastEffect = e.next = e : (r = n.next, n.next = e, e.next = r, t.lastEffect = e), e;
	}
	function is() {
		return Oo().memoizedState;
	}
	function as(e, t, n, r) {
		var i = Do();
		W.flags |= e, i.memoizedState = rs(1 | t, { destroy: void 0 }, n, r === void 0 ? null : r);
	}
	function os(e, t, n, r) {
		var i = Oo();
		r = r === void 0 ? null : r;
		var a = i.memoizedState.inst;
		G !== null && r !== null && yo(r, G.memoizedState.deps) ? i.memoizedState = rs(t, a, n, r) : (W.flags |= e, i.memoizedState = rs(1 | t, a, n, r));
	}
	function ss(e, t) {
		as(8390656, 8, e, t);
	}
	function cs(e, t) {
		os(2048, 8, e, t);
	}
	function ls(e) {
		W.flags |= 4;
		var t = W.updateQueue;
		if (t === null) t = ko(), W.updateQueue = t, t.events = [e];
		else {
			var n = t.events;
			n === null ? t.events = [e] : n.push(e);
		}
	}
	function us(e) {
		var t = Oo().memoizedState;
		return ls({
			ref: t,
			nextImpl: e
		}), function() {
			if (K & 2) throw Error(a(440));
			return t.impl.apply(void 0, arguments);
		};
	}
	function ds(e, t) {
		return os(4, 2, e, t);
	}
	function fs(e, t) {
		return os(4, 4, e, t);
	}
	function ps(e, t) {
		if (typeof t == "function") {
			e = e();
			var n = t(e);
			return function() {
				typeof n == "function" ? n() : t(null);
			};
		}
		if (t != null) return e = e(), t.current = e, function() {
			t.current = null;
		};
	}
	function ms(e, t, n) {
		n = n == null ? null : n.concat([e]), os(4, 4, ps.bind(null, t, e), n);
	}
	function hs() {}
	function gs(e, t) {
		var n = Oo();
		t = t === void 0 ? null : t;
		var r = n.memoizedState;
		return t !== null && yo(t, r[1]) ? r[0] : (n.memoizedState = [e, t], e);
	}
	function _s(e, t) {
		var n = Oo();
		t = t === void 0 ? null : t;
		var r = n.memoizedState;
		if (t !== null && yo(t, r[1])) return r[0];
		if (r = e(), po) {
			Le(!0);
			try {
				e();
			} finally {
				Le(!1);
			}
		}
		return n.memoizedState = [r, t], r;
	}
	function vs(e, t, n) {
		return n === void 0 || co & 1073741824 && !(Y & 261930) ? e.memoizedState = t : (e.memoizedState = n, e = mu(), W.lanes |= e, Gl |= e, n);
	}
	function ys(e, t, n, r) {
		return Sr(n, t) ? n : Ja.current === null ? !(co & 42) || co & 1073741824 && !(Y & 261930) ? (tc = !0, e.memoizedState = n) : (e = mu(), W.lanes |= e, Gl |= e, t) : (e = vs(e, n, r), Sr(e, t) || (tc = !0), e);
	}
	function bs(e, t, n, r, i) {
		var a = P.p;
		P.p = a !== 0 && 8 > a ? a : 8;
		var o = N.T, s = {};
		N.T = s, Ms(e, !1, t, n);
		try {
			var c = i(), l = N.S;
			l !== null && l(s, c), typeof c == "object" && c && typeof c.then == "function" ? js(e, t, ha(c, r), pu(e)) : js(e, t, r, pu(e));
		} catch (n) {
			js(e, t, {
				then: function() {},
				status: "rejected",
				reason: n
			}, pu());
		} finally {
			P.p = a, o !== null && s.types !== null && (o.types = s.types), N.T = o;
		}
	}
	function xs() {}
	function Ss(e, t, n, r) {
		if (e.tag !== 5) throw Error(a(476));
		var i = Cs(e).queue;
		bs(e, i, t, re, n === null ? xs : function() {
			return ws(e), n(r);
		});
	}
	function Cs(e) {
		var t = e.memoizedState;
		if (t !== null) return t;
		t = {
			memoizedState: re,
			baseState: re,
			baseQueue: null,
			queue: {
				pending: null,
				lanes: 0,
				dispatch: null,
				lastRenderedReducer: No,
				lastRenderedState: re
			},
			next: null
		};
		var n = {};
		return t.next = {
			memoizedState: n,
			baseState: n,
			baseQueue: null,
			queue: {
				pending: null,
				lanes: 0,
				dispatch: null,
				lastRenderedReducer: No,
				lastRenderedState: n
			},
			next: null
		}, e.memoizedState = t, e = e.alternate, e !== null && (e.memoizedState = t), t;
	}
	function ws(e) {
		var t = Cs(e);
		t.next === null && (t = e.alternate.memoizedState), js(e, t.next.queue, {}, pu());
	}
	function Ts() {
		return ea(Qf);
	}
	function Es() {
		return Oo().memoizedState;
	}
	function Ds() {
		return Oo().memoizedState;
	}
	function Os(e) {
		for (var t = e.return; t !== null;) {
			switch (t.tag) {
				case 24:
				case 3:
					var n = pu();
					e = za(n);
					var r = Ba(t, e, n);
					r !== null && (hu(r, t, n), Va(r, t, n)), t = { cache: sa() }, e.payload = t;
					return;
			}
			t = t.return;
		}
	}
	function ks(e, t, n) {
		var r = pu();
		n = {
			lane: r,
			revertLane: 0,
			gesture: null,
			action: n,
			hasEagerState: !1,
			eagerState: null,
			next: null
		}, Ns(e) ? Ps(t, n) : (n = ni(e, t, n, r), n !== null && (hu(n, e, r), Fs(n, t, r)));
	}
	function As(e, t, n) {
		js(e, t, n, pu());
	}
	function js(e, t, n, r) {
		var i = {
			lane: r,
			revertLane: 0,
			gesture: null,
			action: n,
			hasEagerState: !1,
			eagerState: null,
			next: null
		};
		if (Ns(e)) Ps(t, i);
		else {
			var a = e.alternate;
			if (e.lanes === 0 && (a === null || a.lanes === 0) && (a = t.lastRenderedReducer, a !== null)) try {
				var o = t.lastRenderedState, s = a(o, n);
				if (i.hasEagerState = !0, i.eagerState = s, Sr(s, o)) return ti(e, t, i, 0), q === null && ei(), !1;
			} catch {}
			if (n = ni(e, t, i, r), n !== null) return hu(n, e, r), Fs(n, t, r), !0;
		}
		return !1;
	}
	function Ms(e, t, n, r) {
		if (r = {
			lane: 2,
			revertLane: dd(),
			gesture: null,
			action: r,
			hasEagerState: !1,
			eagerState: null,
			next: null
		}, Ns(e)) {
			if (t) throw Error(a(479));
		} else t = ni(e, n, r, 2), t !== null && hu(t, e, 2);
	}
	function Ns(e) {
		var t = e.alternate;
		return e === W || t !== null && t === W;
	}
	function Ps(e, t) {
		fo = uo = !0;
		var n = e.pending;
		n === null ? t.next = t : (t.next = n.next, n.next = t), e.pending = t;
	}
	function Fs(e, t, n) {
		if (n & 4194048) {
			var r = t.lanes;
			r &= e.pendingLanes, n |= r, t.lanes = n, et(e, n);
		}
	}
	var Is = {
		readContext: ea,
		use: jo,
		useCallback: vo,
		useContext: vo,
		useEffect: vo,
		useImperativeHandle: vo,
		useLayoutEffect: vo,
		useInsertionEffect: vo,
		useMemo: vo,
		useReducer: vo,
		useRef: vo,
		useState: vo,
		useDebugValue: vo,
		useDeferredValue: vo,
		useTransition: vo,
		useSyncExternalStore: vo,
		useId: vo,
		useHostTransitionStatus: vo,
		useFormState: vo,
		useActionState: vo,
		useOptimistic: vo,
		useMemoCache: vo,
		useCacheRefresh: vo
	};
	Is.useEffectEvent = vo;
	var Ls = {
		readContext: ea,
		use: jo,
		useCallback: function(e, t) {
			return Do().memoizedState = [e, t === void 0 ? null : t], e;
		},
		useContext: ea,
		useEffect: ss,
		useImperativeHandle: function(e, t, n) {
			n = n == null ? null : n.concat([e]), as(4194308, 4, ps.bind(null, t, e), n);
		},
		useLayoutEffect: function(e, t) {
			return as(4194308, 4, e, t);
		},
		useInsertionEffect: function(e, t) {
			as(4, 2, e, t);
		},
		useMemo: function(e, t) {
			var n = Do();
			t = t === void 0 ? null : t;
			var r = e();
			if (po) {
				Le(!0);
				try {
					e();
				} finally {
					Le(!1);
				}
			}
			return n.memoizedState = [r, t], r;
		},
		useReducer: function(e, t, n) {
			var r = Do();
			if (n !== void 0) {
				var i = n(t);
				if (po) {
					Le(!0);
					try {
						n(t);
					} finally {
						Le(!1);
					}
				}
			} else i = t;
			return r.memoizedState = r.baseState = i, e = {
				pending: null,
				lanes: 0,
				dispatch: null,
				lastRenderedReducer: e,
				lastRenderedState: i
			}, r.queue = e, e = e.dispatch = ks.bind(null, W, e), [r.memoizedState, e];
		},
		useRef: function(e) {
			var t = Do();
			return e = { current: e }, t.memoizedState = e;
		},
		useState: function(e) {
			e = Uo(e);
			var t = e.queue, n = As.bind(null, W, t);
			return t.dispatch = n, [e.memoizedState, n];
		},
		useDebugValue: hs,
		useDeferredValue: function(e, t) {
			return vs(Do(), e, t);
		},
		useTransition: function() {
			var e = Uo(!1);
			return e = bs.bind(null, W, e.queue, !0, !1), Do().memoizedState = e, [!1, e];
		},
		useSyncExternalStore: function(e, t, n) {
			var r = W, i = Do();
			if (B) {
				if (n === void 0) throw Error(a(407));
				n = n();
			} else {
				if (n = t(), q === null) throw Error(a(349));
				Y & 127 || Ro(r, t, n);
			}
			i.memoizedState = n;
			var o = {
				value: n,
				getSnapshot: t
			};
			return i.queue = o, ss(Bo.bind(null, r, o, e), [e]), r.flags |= 2048, rs(9, { destroy: void 0 }, zo.bind(null, r, o, n, t), null), n;
		},
		useId: function() {
			var e = Do(), t = q.identifierPrefix;
			if (B) {
				var n = Di, r = Ei;
				n = (r & ~(1 << 32 - Re(r) - 1)).toString(32) + n, t = "_" + t + "R_" + n, n = mo++, 0 < n && (t += "H" + n.toString(32)), t += "_";
			} else n = _o++, t = "_" + t + "r_" + n.toString(32) + "_";
			return e.memoizedState = t;
		},
		useHostTransitionStatus: Ts,
		useFormState: Qo,
		useActionState: Qo,
		useOptimistic: function(e) {
			var t = Do();
			t.memoizedState = t.baseState = e;
			var n = {
				pending: null,
				lanes: 0,
				dispatch: null,
				lastRenderedReducer: null,
				lastRenderedState: null
			};
			return t.queue = n, t = Ms.bind(null, W, !0, n), n.dispatch = t, [e, t];
		},
		useMemoCache: Mo,
		useCacheRefresh: function() {
			return Do().memoizedState = Os.bind(null, W);
		},
		useEffectEvent: function(e) {
			var t = Do(), n = { impl: e };
			return t.memoizedState = n, function() {
				if (K & 2) throw Error(a(440));
				return n.impl.apply(void 0, arguments);
			};
		}
	}, Rs = {
		readContext: ea,
		use: jo,
		useCallback: gs,
		useContext: ea,
		useEffect: cs,
		useImperativeHandle: ms,
		useInsertionEffect: ds,
		useLayoutEffect: fs,
		useMemo: _s,
		useReducer: Po,
		useRef: is,
		useState: function() {
			return Po(No);
		},
		useDebugValue: hs,
		useDeferredValue: function(e, t) {
			return ys(Oo(), G.memoizedState, e, t);
		},
		useTransition: function() {
			var e = Po(No)[0], t = Oo().memoizedState;
			return [typeof e == "boolean" ? e : Ao(e), t];
		},
		useSyncExternalStore: Lo,
		useId: Es,
		useHostTransitionStatus: Ts,
		useFormState: $o,
		useActionState: $o,
		useOptimistic: function(e, t) {
			return Wo(Oo(), G, e, t);
		},
		useMemoCache: Mo,
		useCacheRefresh: Ds
	};
	Rs.useEffectEvent = us;
	var zs = {
		readContext: ea,
		use: jo,
		useCallback: gs,
		useContext: ea,
		useEffect: cs,
		useImperativeHandle: ms,
		useInsertionEffect: ds,
		useLayoutEffect: fs,
		useMemo: _s,
		useReducer: Io,
		useRef: is,
		useState: function() {
			return Io(No);
		},
		useDebugValue: hs,
		useDeferredValue: function(e, t) {
			var n = Oo();
			return G === null ? vs(n, e, t) : ys(n, G.memoizedState, e, t);
		},
		useTransition: function() {
			var e = Io(No)[0], t = Oo().memoizedState;
			return [typeof e == "boolean" ? e : Ao(e), t];
		},
		useSyncExternalStore: Lo,
		useId: Es,
		useHostTransitionStatus: Ts,
		useFormState: ns,
		useActionState: ns,
		useOptimistic: function(e, t) {
			var n = Oo();
			return G === null ? (n.baseState = e, [e, n.queue.dispatch]) : Wo(n, G, e, t);
		},
		useMemoCache: Mo,
		useCacheRefresh: Ds
	};
	zs.useEffectEvent = us;
	function Bs(e, t, n, r) {
		t = e.memoizedState, n = n(r, t), n = n == null ? t : h({}, t, n), e.memoizedState = n, e.lanes === 0 && (e.updateQueue.baseState = n);
	}
	var Vs = {
		enqueueSetState: function(e, t, n) {
			e = e._reactInternals;
			var r = pu(), i = za(r);
			i.payload = t, n != null && (i.callback = n), t = Ba(e, i, r), t !== null && (hu(t, e, r), Va(t, e, r));
		},
		enqueueReplaceState: function(e, t, n) {
			e = e._reactInternals;
			var r = pu(), i = za(r);
			i.tag = 1, i.payload = t, n != null && (i.callback = n), t = Ba(e, i, r), t !== null && (hu(t, e, r), Va(t, e, r));
		},
		enqueueForceUpdate: function(e, t) {
			e = e._reactInternals;
			var n = pu(), r = za(n);
			r.tag = 2, t != null && (r.callback = t), t = Ba(e, r, n), t !== null && (hu(t, e, n), Va(t, e, n));
		}
	};
	function Hs(e, t, n, r, i, a, o) {
		return e = e.stateNode, typeof e.shouldComponentUpdate == "function" ? e.shouldComponentUpdate(r, a, o) : t.prototype && t.prototype.isPureReactComponent ? !Cr(n, r) || !Cr(i, a) : !0;
	}
	function Us(e, t, n, r) {
		e = t.state, typeof t.componentWillReceiveProps == "function" && t.componentWillReceiveProps(n, r), typeof t.UNSAFE_componentWillReceiveProps == "function" && t.UNSAFE_componentWillReceiveProps(n, r), t.state !== e && Vs.enqueueReplaceState(t, t.state, null);
	}
	function Ws(e, t) {
		var n = t;
		if ("ref" in t) for (var r in n = {}, t) r !== "ref" && (n[r] = t[r]);
		if (e = e.defaultProps) for (var i in n === t && (n = h({}, n)), e) n[i] === void 0 && (n[i] = e[i]);
		return n;
	}
	function Gs(e) {
		Xr(e);
	}
	function Ks(e) {
		console.error(e);
	}
	function qs(e) {
		Xr(e);
	}
	function Js(e, t) {
		try {
			var n = e.onUncaughtError;
			n(t.value, { componentStack: t.stack });
		} catch (e) {
			setTimeout(function() {
				throw e;
			});
		}
	}
	function Ys(e, t, n) {
		try {
			var r = e.onCaughtError;
			r(n.value, {
				componentStack: n.stack,
				errorBoundary: t.tag === 1 ? t.stateNode : null
			});
		} catch (e) {
			setTimeout(function() {
				throw e;
			});
		}
	}
	function Xs(e, t, n) {
		return n = za(n), n.tag = 3, n.payload = { element: null }, n.callback = function() {
			Js(e, t);
		}, n;
	}
	function Zs(e) {
		return e = za(e), e.tag = 3, e;
	}
	function Qs(e, t, n, r) {
		var i = n.type.getDerivedStateFromError;
		if (typeof i == "function") {
			var a = r.value;
			e.payload = function() {
				return i(a);
			}, e.callback = function() {
				Ys(t, n, r);
			};
		}
		var o = n.stateNode;
		o !== null && typeof o.componentDidCatch == "function" && (e.callback = function() {
			Ys(t, n, r), typeof i != "function" && (ru === null ? ru = /* @__PURE__ */ new Set([this]) : ru.add(this));
			var e = r.stack;
			this.componentDidCatch(r.value, { componentStack: e === null ? "" : e });
		});
	}
	function $s(e, t, n, r, i) {
		if (n.flags |= 32768, typeof r == "object" && r && typeof r.then == "function") {
			if (t = n.alternate, t !== null && Zi(t, n, i, !0), n = $a.current, n !== null) {
				switch (n.tag) {
					case 31:
					case 13: return eo === null ? Du() : n.alternate === null && Wl === 0 && (Wl = 3), n.flags &= -257, n.flags |= 65536, n.lanes = i, r === xa ? n.flags |= 16384 : (t = n.updateQueue, t === null ? n.updateQueue = /* @__PURE__ */ new Set([r]) : t.add(r), Gu(e, r, i)), !1;
					case 22: return n.flags |= 65536, r === xa ? n.flags |= 16384 : (t = n.updateQueue, t === null ? (t = {
						transitions: null,
						markerInstances: null,
						retryQueue: /* @__PURE__ */ new Set([r])
					}, n.updateQueue = t) : (n = t.retryQueue, n === null ? t.retryQueue = /* @__PURE__ */ new Set([r]) : n.add(r)), Gu(e, r, i)), !1;
				}
				throw Error(a(435, n.tag));
			}
			return Gu(e, r, i), Du(), !1;
		}
		if (B) return t = $a.current, t === null ? (r !== Ii && (t = Error(a(423), { cause: r }), Ui(vi(t, n))), e = e.current.alternate, e.flags |= 65536, i &= -i, e.lanes |= i, r = vi(r, n), i = Xs(e.stateNode, r, i), Ha(e, i), Wl !== 4 && (Wl = 2)) : (!(t.flags & 65536) && (t.flags |= 256), t.flags |= 65536, t.lanes = i, r !== Ii && (e = Error(a(422), { cause: r }), Ui(vi(e, n)))), !1;
		var o = Error(a(520), { cause: r });
		if (o = vi(o, n), Xl === null ? Xl = [o] : Xl.push(o), Wl !== 4 && (Wl = 2), t === null) return !0;
		r = vi(r, n), n = t;
		do {
			switch (n.tag) {
				case 3: return n.flags |= 65536, e = i & -i, n.lanes |= e, e = Xs(n.stateNode, r, e), Ha(n, e), !1;
				case 1: if (t = n.type, o = n.stateNode, !(n.flags & 128) && (typeof t.getDerivedStateFromError == "function" || o !== null && typeof o.componentDidCatch == "function" && (ru === null || !ru.has(o)))) return n.flags |= 65536, i &= -i, n.lanes |= i, i = Zs(i), Qs(i, e, n, r), Ha(n, i), !1;
			}
			n = n.return;
		} while (n !== null);
		return !1;
	}
	var ec = Error(a(461)), tc = !1;
	function nc(e, t, n, r) {
		t.child = e === null ? Fa(t, null, n, r) : Pa(t, e.child, n, r);
	}
	function rc(e, t, n, r, i) {
		n = n.render;
		var a = t.ref;
		if ("ref" in r) {
			var o = {};
			for (var s in r) s !== "ref" && (o[s] = r[s]);
		} else o = r;
		return $i(t), r = bo(e, t, n, o, a, i), s = wo(), e !== null && !tc ? (To(e, t, i), Dc(e, t, i)) : (B && s && Ai(t), t.flags |= 1, nc(e, t, r, i), t.child);
	}
	function ic(e, t, n, r, i) {
		if (e === null) {
			var a = n.type;
			return typeof a == "function" && !li(a) && a.defaultProps === void 0 && n.compare === null ? (t.tag = 15, t.type = a, ac(e, t, a, r, i)) : (e = fi(n.type, null, r, t, t.mode, i), e.ref = t.ref, e.return = t, t.child = e);
		}
		if (a = e.child, !Oc(e, i)) {
			var o = a.memoizedProps;
			if (n = n.compare, n = n === null ? Cr : n, n(o, r) && e.ref === t.ref) return Dc(e, t, i);
		}
		return t.flags |= 1, e = ui(a, r), e.ref = t.ref, e.return = t, t.child = e;
	}
	function ac(e, t, n, r, i) {
		if (e !== null) {
			var a = e.memoizedProps;
			if (Cr(a, r) && e.ref === t.ref) {
				if (tc = !1, t.pendingProps = r = a, Oc(e, i)) e.flags & 131072 && (tc = !0);
				else return t.lanes = e.lanes, Dc(e, t, i);
			}
		}
		return pc(e, t, n, r, i);
	}
	function oc(e, t, n, r) {
		var i = r.children, a = e === null ? null : e.memoizedState;
		if (e === null && t.stateNode === null && (t.stateNode = {
			_visibility: 1,
			_pendingMarkers: null,
			_retryCache: null,
			_transitions: null
		}), r.mode === "hidden") {
			if (t.flags & 128) {
				if (a = a === null ? n : a.baseLanes | n, e !== null) {
					for (r = t.child = e.child, i = 0; r !== null;) i = i | r.lanes | r.childLanes, r = r.sibling;
					r = i & ~a;
				} else r = 0, t.child = null;
				return cc(e, t, a, n, r);
			}
			if (n & 536870912) t.memoizedState = {
				baseLanes: 0,
				cachePool: null
			}, e !== null && va(t, a === null ? null : a.cachePool), a === null ? Za() : Xa(t, a), ro(t);
			else return r = t.lanes = 536870912, cc(e, t, a === null ? n : a.baseLanes | n, n, r);
		} else a === null ? (e !== null && va(t, null), Za(), io(t)) : (va(t, a.cachePool), Xa(t, a), io(t), t.memoizedState = null);
		return nc(e, t, i, n), t.child;
	}
	function sc(e, t) {
		return e !== null && e.tag === 22 || t.stateNode !== null || (t.stateNode = {
			_visibility: 1,
			_pendingMarkers: null,
			_retryCache: null,
			_transitions: null
		}), t.sibling;
	}
	function cc(e, t, n, r, i) {
		var a = _a();
		return a = a === null ? null : {
			parent: oa._currentValue,
			pool: a
		}, t.memoizedState = {
			baseLanes: n,
			cachePool: a
		}, e !== null && va(t, null), Za(), ro(t), e !== null && Zi(e, t, r, !0), t.childLanes = i, null;
	}
	function lc(e, t) {
		return t = Sc({
			mode: t.mode,
			children: t.children
		}, e.mode), t.ref = e.ref, e.child = t, t.return = e, t;
	}
	function uc(e, t, n) {
		return Pa(t, e.child, null, n), e = lc(t, t.pendingProps), e.flags |= 2, ao(t), t.memoizedState = null, e;
	}
	function dc(e, t, n) {
		var r = t.pendingProps, i = !!(t.flags & 128);
		if (t.flags &= -129, e === null) {
			if (B) {
				if (r.mode === "hidden") return e = lc(t, r), t.lanes = 536870912, sc(null, e);
				if (no(t), (e = z) ? (e = rf(e, Fi), e = e !== null && e.data === "&" ? e : null, e !== null && (t.memoizedState = {
					dehydrated: e,
					treeContext: Ti === null ? null : {
						id: Ei,
						overflow: Di
					},
					retryLane: 536870912,
					hydrationErrors: null
				}, n = hi(e), n.return = t, t.child = n, Ni = t, z = null)) : e = null, e === null) throw Li(t);
				return t.lanes = 536870912, null;
			}
			return lc(t, r);
		}
		var o = e.memoizedState;
		if (o !== null) {
			var s = o.dehydrated;
			if (no(t), i) {
				if (t.flags & 256) t.flags &= -257, t = uc(e, t, n);
				else if (t.memoizedState !== null) t.child = e.child, t.flags |= 128, t = null;
				else throw Error(a(558));
			} else if (tc || Zi(e, t, n, !1), i = (n & e.childLanes) !== 0, tc || i) {
				if (r = q, r !== null && (s = tt(r, n), s !== 0 && s !== o.retryLane)) throw o.retryLane = s, ri(e, s), hu(r, e, s), ec;
				Du(), t = uc(e, t, n);
			} else e = o.treeContext, z = cf(s.nextSibling), Ni = t, B = !0, Pi = null, Fi = !1, e !== null && Mi(t, e), t = lc(t, r), t.flags |= 4096;
			return t;
		}
		return e = ui(e.child, {
			mode: r.mode,
			children: r.children
		}), e.ref = t.ref, t.child = e, e.return = t, e;
	}
	function fc(e, t) {
		var n = t.ref;
		if (n === null) e !== null && e.ref !== null && (t.flags |= 4194816);
		else {
			if (typeof n != "function" && typeof n != "object") throw Error(a(284));
			(e === null || e.ref !== n) && (t.flags |= 4194816);
		}
	}
	function pc(e, t, n, r, i) {
		return $i(t), n = bo(e, t, n, r, void 0, i), r = wo(), e !== null && !tc ? (To(e, t, i), Dc(e, t, i)) : (B && r && Ai(t), t.flags |= 1, nc(e, t, n, i), t.child);
	}
	function mc(e, t, n, r, i, a) {
		return $i(t), t.updateQueue = null, n = So(t, r, n, i), xo(e), r = wo(), e !== null && !tc ? (To(e, t, a), Dc(e, t, a)) : (B && r && Ai(t), t.flags |= 1, nc(e, t, n, a), t.child);
	}
	function hc(e, t, n, r, i) {
		if ($i(t), t.stateNode === null) {
			var a = oi, o = n.contextType;
			typeof o == "object" && o && (a = ea(o)), a = new n(r, a), t.memoizedState = a.state !== null && a.state !== void 0 ? a.state : null, a.updater = Vs, t.stateNode = a, a._reactInternals = t, a = t.stateNode, a.props = r, a.state = t.memoizedState, a.refs = {}, La(t), o = n.contextType, a.context = typeof o == "object" && o ? ea(o) : oi, a.state = t.memoizedState, o = n.getDerivedStateFromProps, typeof o == "function" && (Bs(t, n, o, r), a.state = t.memoizedState), typeof n.getDerivedStateFromProps == "function" || typeof a.getSnapshotBeforeUpdate == "function" || typeof a.UNSAFE_componentWillMount != "function" && typeof a.componentWillMount != "function" || (o = a.state, typeof a.componentWillMount == "function" && a.componentWillMount(), typeof a.UNSAFE_componentWillMount == "function" && a.UNSAFE_componentWillMount(), o !== a.state && Vs.enqueueReplaceState(a, a.state, null), Ga(t, r, a, i), Wa(), a.state = t.memoizedState), typeof a.componentDidMount == "function" && (t.flags |= 4194308), r = !0;
		} else if (e === null) {
			a = t.stateNode;
			var s = t.memoizedProps, c = Ws(n, s);
			a.props = c;
			var l = a.context, u = n.contextType;
			o = oi, typeof u == "object" && u && (o = ea(u));
			var d = n.getDerivedStateFromProps;
			u = typeof d == "function" || typeof a.getSnapshotBeforeUpdate == "function", s = t.pendingProps !== s, u || typeof a.UNSAFE_componentWillReceiveProps != "function" && typeof a.componentWillReceiveProps != "function" || (s || l !== o) && Us(t, a, r, o), Ia = !1;
			var f = t.memoizedState;
			a.state = f, Ga(t, r, a, i), Wa(), l = t.memoizedState, s || f !== l || Ia ? (typeof d == "function" && (Bs(t, n, d, r), l = t.memoizedState), (c = Ia || Hs(t, n, c, r, f, l, o)) ? (u || typeof a.UNSAFE_componentWillMount != "function" && typeof a.componentWillMount != "function" || (typeof a.componentWillMount == "function" && a.componentWillMount(), typeof a.UNSAFE_componentWillMount == "function" && a.UNSAFE_componentWillMount()), typeof a.componentDidMount == "function" && (t.flags |= 4194308)) : (typeof a.componentDidMount == "function" && (t.flags |= 4194308), t.memoizedProps = r, t.memoizedState = l), a.props = r, a.state = l, a.context = o, r = c) : (typeof a.componentDidMount == "function" && (t.flags |= 4194308), r = !1);
		} else {
			a = t.stateNode, Ra(e, t), o = t.memoizedProps, u = Ws(n, o), a.props = u, d = t.pendingProps, f = a.context, l = n.contextType, c = oi, typeof l == "object" && l && (c = ea(l)), s = n.getDerivedStateFromProps, (l = typeof s == "function" || typeof a.getSnapshotBeforeUpdate == "function") || typeof a.UNSAFE_componentWillReceiveProps != "function" && typeof a.componentWillReceiveProps != "function" || (o !== d || f !== c) && Us(t, a, r, c), Ia = !1, f = t.memoizedState, a.state = f, Ga(t, r, a, i), Wa();
			var p = t.memoizedState;
			o !== d || f !== p || Ia || e !== null && e.dependencies !== null && Qi(e.dependencies) ? (typeof s == "function" && (Bs(t, n, s, r), p = t.memoizedState), (u = Ia || Hs(t, n, u, r, f, p, c) || e !== null && e.dependencies !== null && Qi(e.dependencies)) ? (l || typeof a.UNSAFE_componentWillUpdate != "function" && typeof a.componentWillUpdate != "function" || (typeof a.componentWillUpdate == "function" && a.componentWillUpdate(r, p, c), typeof a.UNSAFE_componentWillUpdate == "function" && a.UNSAFE_componentWillUpdate(r, p, c)), typeof a.componentDidUpdate == "function" && (t.flags |= 4), typeof a.getSnapshotBeforeUpdate == "function" && (t.flags |= 1024)) : (typeof a.componentDidUpdate != "function" || o === e.memoizedProps && f === e.memoizedState || (t.flags |= 4), typeof a.getSnapshotBeforeUpdate != "function" || o === e.memoizedProps && f === e.memoizedState || (t.flags |= 1024), t.memoizedProps = r, t.memoizedState = p), a.props = r, a.state = p, a.context = c, r = u) : (typeof a.componentDidUpdate != "function" || o === e.memoizedProps && f === e.memoizedState || (t.flags |= 4), typeof a.getSnapshotBeforeUpdate != "function" || o === e.memoizedProps && f === e.memoizedState || (t.flags |= 1024), r = !1);
		}
		return a = r, fc(e, t), r = !!(t.flags & 128), a || r ? (a = t.stateNode, n = r && typeof n.getDerivedStateFromError != "function" ? null : a.render(), t.flags |= 1, e !== null && r ? (t.child = Pa(t, e.child, null, i), t.child = Pa(t, null, n, i)) : nc(e, t, n, i), t.memoizedState = a.state, e = t.child) : e = Dc(e, t, i), e;
	}
	function gc(e, t, n, r) {
		return Vi(), t.flags |= 256, nc(e, t, n, r), t.child;
	}
	var _c = {
		dehydrated: null,
		treeContext: null,
		retryLane: 0,
		hydrationErrors: null
	};
	function vc(e) {
		return {
			baseLanes: e,
			cachePool: H()
		};
	}
	function yc(e, t, n) {
		return e = e === null ? 0 : e.childLanes & ~n, t && (e |= Jl), e;
	}
	function bc(e, t, n) {
		var r = t.pendingProps, i = !1, o = !!(t.flags & 128), s;
		if ((s = o) || (s = e !== null && e.memoizedState === null ? !1 : !!(oo.current & 2)), s && (i = !0, t.flags &= -129), s = !!(t.flags & 32), t.flags &= -33, e === null) {
			if (B) {
				if (i ? to(t) : io(t), (e = z) ? (e = rf(e, Fi), e = e !== null && e.data !== "&" ? e : null, e !== null && (t.memoizedState = {
					dehydrated: e,
					treeContext: Ti === null ? null : {
						id: Ei,
						overflow: Di
					},
					retryLane: 536870912,
					hydrationErrors: null
				}, n = hi(e), n.return = t, t.child = n, Ni = t, z = null)) : e = null, e === null) throw Li(t);
				return of(e) ? t.lanes = 32 : t.lanes = 536870912, null;
			}
			var c = r.children;
			return r = r.fallback, i ? (io(t), i = t.mode, c = Sc({
				mode: "hidden",
				children: c
			}, i), r = pi(r, i, n, null), c.return = t, r.return = t, c.sibling = r, t.child = c, r = t.child, r.memoizedState = vc(n), r.childLanes = yc(e, s, n), t.memoizedState = _c, sc(null, r)) : (to(t), xc(t, c));
		}
		var l = e.memoizedState;
		if (l !== null && (c = l.dehydrated, c !== null)) {
			if (o) t.flags & 256 ? (to(t), t.flags &= -257, t = Cc(e, t, n)) : t.memoizedState === null ? (io(t), c = r.fallback, i = t.mode, r = Sc({
				mode: "visible",
				children: r.children
			}, i), c = pi(c, i, n, null), c.flags |= 2, r.return = t, c.return = t, r.sibling = c, t.child = r, Pa(t, e.child, null, n), r = t.child, r.memoizedState = vc(n), r.childLanes = yc(e, s, n), t.memoizedState = _c, t = sc(null, r)) : (io(t), t.child = e.child, t.flags |= 128, t = null);
			else if (to(t), of(c)) {
				if (s = c.nextSibling && c.nextSibling.dataset, s) var u = s.dgst;
				s = u, r = Error(a(419)), r.stack = "", r.digest = s, Ui({
					value: r,
					source: null,
					stack: null
				}), t = Cc(e, t, n);
			} else if (tc || Zi(e, t, n, !1), s = (n & e.childLanes) !== 0, tc || s) {
				if (s = q, s !== null && (r = tt(s, n), r !== 0 && r !== l.retryLane)) throw l.retryLane = r, ri(e, r), hu(s, e, r), ec;
				af(c) || Du(), t = Cc(e, t, n);
			} else af(c) ? (t.flags |= 192, t.child = e.child, t = null) : (e = l.treeContext, z = cf(c.nextSibling), Ni = t, B = !0, Pi = null, Fi = !1, e !== null && Mi(t, e), t = xc(t, r.children), t.flags |= 4096);
			return t;
		}
		return i ? (io(t), c = r.fallback, i = t.mode, l = e.child, u = l.sibling, r = ui(l, {
			mode: "hidden",
			children: r.children
		}), r.subtreeFlags = l.subtreeFlags & 65011712, u === null ? (c = pi(c, i, n, null), c.flags |= 2) : c = ui(u, c), c.return = t, r.return = t, r.sibling = c, t.child = r, sc(null, r), r = t.child, c = e.child.memoizedState, c === null ? c = vc(n) : (i = c.cachePool, i === null ? i = H() : (l = oa._currentValue, i = i.parent === l ? i : {
			parent: l,
			pool: l
		}), c = {
			baseLanes: c.baseLanes | n,
			cachePool: i
		}), r.memoizedState = c, r.childLanes = yc(e, s, n), t.memoizedState = _c, sc(e.child, r)) : (to(t), n = e.child, e = n.sibling, n = ui(n, {
			mode: "visible",
			children: r.children
		}), n.return = t, n.sibling = null, e !== null && (s = t.deletions, s === null ? (t.deletions = [e], t.flags |= 16) : s.push(e)), t.child = n, t.memoizedState = null, n);
	}
	function xc(e, t) {
		return t = Sc({
			mode: "visible",
			children: t
		}, e.mode), t.return = e, e.child = t;
	}
	function Sc(e, t) {
		return e = ci(22, e, null, t), e.lanes = 0, e;
	}
	function Cc(e, t, n) {
		return Pa(t, e.child, null, n), e = xc(t, t.pendingProps.children), e.flags |= 2, t.memoizedState = null, e;
	}
	function wc(e, t, n) {
		e.lanes |= t;
		var r = e.alternate;
		r !== null && (r.lanes |= t), Yi(e.return, t, n);
	}
	function Tc(e, t, n, r, i, a) {
		var o = e.memoizedState;
		o === null ? e.memoizedState = {
			isBackwards: t,
			rendering: null,
			renderingStartTime: 0,
			last: r,
			tail: n,
			tailMode: i,
			treeForkCount: a
		} : (o.isBackwards = t, o.rendering = null, o.renderingStartTime = 0, o.last = r, o.tail = n, o.tailMode = i, o.treeForkCount = a);
	}
	function Ec(e, t, n) {
		var r = t.pendingProps, i = r.revealOrder, a = r.tail;
		r = r.children;
		var o = oo.current, s = !!(o & 2);
		if (s ? (o = o & 1 | 2, t.flags |= 128) : o &= 1, I(oo, o), nc(e, t, r, n), r = B ? Si : 0, !s && e !== null && e.flags & 128) a: for (e = t.child; e !== null;) {
			if (e.tag === 13) e.memoizedState !== null && wc(e, n, t);
			else if (e.tag === 19) wc(e, n, t);
			else if (e.child !== null) {
				e.child.return = e, e = e.child;
				continue;
			}
			if (e === t) break a;
			for (; e.sibling === null;) {
				if (e.return === null || e.return === t) break a;
				e = e.return;
			}
			e.sibling.return = e.return, e = e.sibling;
		}
		switch (i) {
			case "forwards":
				for (n = t.child, i = null; n !== null;) e = n.alternate, e !== null && so(e) === null && (i = n), n = n.sibling;
				n = i, n === null ? (i = t.child, t.child = null) : (i = n.sibling, n.sibling = null), Tc(t, !1, i, n, a, r);
				break;
			case "backwards":
			case "unstable_legacy-backwards":
				for (n = null, i = t.child, t.child = null; i !== null;) {
					if (e = i.alternate, e !== null && so(e) === null) {
						t.child = i;
						break;
					}
					e = i.sibling, i.sibling = n, n = i, i = e;
				}
				Tc(t, !0, n, null, a, r);
				break;
			case "together":
				Tc(t, !1, null, null, void 0, r);
				break;
			default: t.memoizedState = null;
		}
		return t.child;
	}
	function Dc(e, t, n) {
		if (e !== null && (t.dependencies = e.dependencies), Gl |= t.lanes, (n & t.childLanes) === 0) {
			if (e !== null) {
				if (Zi(e, t, n, !1), (n & t.childLanes) === 0) return null;
			} else return null;
		}
		if (e !== null && t.child !== e.child) throw Error(a(153));
		if (t.child !== null) {
			for (e = t.child, n = ui(e, e.pendingProps), t.child = n, n.return = t; e.sibling !== null;) e = e.sibling, n = n.sibling = ui(e, e.pendingProps), n.return = t;
			n.sibling = null;
		}
		return t.child;
	}
	function Oc(e, t) {
		return (e.lanes & t) !== 0 || (e = e.dependencies, !!(e !== null && Qi(e)));
	}
	function kc(e, t, n) {
		switch (t.tag) {
			case 3:
				ue(t, t.stateNode.containerInfo), qi(t, oa, e.memoizedState.cache), Vi();
				break;
			case 27:
			case 5:
				fe(t);
				break;
			case 4:
				ue(t, t.stateNode.containerInfo);
				break;
			case 10:
				qi(t, t.type, t.memoizedProps.value);
				break;
			case 31:
				if (t.memoizedState !== null) return t.flags |= 128, no(t), null;
				break;
			case 13:
				var r = t.memoizedState;
				if (r !== null) return r.dehydrated === null ? (n & t.child.childLanes) === 0 ? (to(t), e = Dc(e, t, n), e === null ? null : e.sibling) : bc(e, t, n) : (to(t), t.flags |= 128, null);
				to(t);
				break;
			case 19:
				var i = !!(e.flags & 128);
				if (r = (n & t.childLanes) !== 0, r ||= (Zi(e, t, n, !1), (n & t.childLanes) !== 0), i) {
					if (r) return Ec(e, t, n);
					t.flags |= 128;
				}
				if (i = t.memoizedState, i !== null && (i.rendering = null, i.tail = null, i.lastEffect = null), I(oo, oo.current), r) break;
				return null;
			case 22: return t.lanes = 0, oc(e, t, n, t.pendingProps);
			case 24: qi(t, oa, e.memoizedState.cache);
		}
		return Dc(e, t, n);
	}
	function Ac(e, t, n) {
		if (e !== null) {
			if (e.memoizedProps !== t.pendingProps) tc = !0;
			else {
				if (!Oc(e, n) && !(t.flags & 128)) return tc = !1, kc(e, t, n);
				tc = !!(e.flags & 131072);
			}
		} else tc = !1, B && t.flags & 1048576 && ki(t, Si, t.index);
		switch (t.lanes = 0, t.tag) {
			case 16:
				a: {
					var r = t.pendingProps;
					if (e = wa(t.elementType), t.type = e, typeof e == "function") li(e) ? (r = Ws(e, r), t.tag = 1, t = hc(null, t, e, r, n)) : (t.tag = 0, t = pc(null, t, e, r, n));
					else {
						if (e != null) {
							var i = e.$$typeof;
							if (i === w) {
								t.tag = 11, t = rc(null, t, e, r, n);
								break a;
							}
							if (i === D) {
								t.tag = 14, t = ic(null, t, e, r, n);
								break a;
							}
						}
						throw t = te(e) || e, Error(a(306, t, ""));
					}
				}
				return t;
			case 0: return pc(e, t, t.type, t.pendingProps, n);
			case 1: return r = t.type, i = Ws(r, t.pendingProps), hc(e, t, r, i, n);
			case 3:
				a: {
					if (ue(t, t.stateNode.containerInfo), e === null) throw Error(a(387));
					r = t.pendingProps;
					var o = t.memoizedState;
					i = o.element, Ra(e, t), Ga(t, r, null, n);
					var s = t.memoizedState;
					if (r = s.cache, qi(t, oa, r), r !== o.cache && Xi(t, [oa], n, !0), Wa(), r = s.element, o.isDehydrated) {
						if (o = {
							element: r,
							isDehydrated: !1,
							cache: s.cache
						}, t.updateQueue.baseState = o, t.memoizedState = o, t.flags & 256) {
							t = gc(e, t, r, n);
							break a;
						}
						if (r !== i) {
							i = vi(Error(a(424)), t), Ui(i), t = gc(e, t, r, n);
							break a;
						}
						switch (e = t.stateNode.containerInfo, e.nodeType) {
							case 9:
								e = e.body;
								break;
							default: e = e.nodeName === "HTML" ? e.ownerDocument.body : e;
						}
						for (z = cf(e.firstChild), Ni = t, B = !0, Pi = null, Fi = !0, n = Fa(t, null, r, n), t.child = n; n;) n.flags = n.flags & -3 | 4096, n = n.sibling;
					} else {
						if (Vi(), r === i) {
							t = Dc(e, t, n);
							break a;
						}
						nc(e, t, r, n);
					}
					t = t.child;
				}
				return t;
			case 26: return fc(e, t), e === null ? (n = kf(t.type, null, t.pendingProps, null)) ? t.memoizedState = n : B || (n = t.type, e = t.pendingProps, r = Bd(ce.current).createElement(n), r[st] = t, r[ct] = e, Pd(r, n, e), bt(r), t.stateNode = r) : t.memoizedState = kf(t.type, e.memoizedProps, t.pendingProps, e.memoizedState), null;
			case 27: return fe(t), e === null && B && (r = t.stateNode = ff(t.type, t.pendingProps, ce.current), Ni = t, Fi = !0, i = z, Zd(t.type) ? (lf = i, z = cf(r.firstChild)) : z = i), nc(e, t, t.pendingProps.children, n), fc(e, t), e === null && (t.flags |= 4194304), t.child;
			case 5: return e === null && B && ((i = r = z) && (r = tf(r, t.type, t.pendingProps, Fi), r === null ? i = !1 : (t.stateNode = r, Ni = t, z = cf(r.firstChild), Fi = !1, i = !0)), i || Li(t)), fe(t), i = t.type, o = t.pendingProps, s = e === null ? null : e.memoizedProps, r = o.children, Ud(i, o) ? r = null : s !== null && Ud(i, s) && (t.flags |= 32), t.memoizedState !== null && (i = bo(e, t, Co, null, null, n), Qf._currentValue = i), fc(e, t), nc(e, t, r, n), t.child;
			case 6: return e === null && B && ((e = n = z) && (n = nf(n, t.pendingProps, Fi), n === null ? e = !1 : (t.stateNode = n, Ni = t, z = null, e = !0)), e || Li(t)), null;
			case 13: return bc(e, t, n);
			case 4: return ue(t, t.stateNode.containerInfo), r = t.pendingProps, e === null ? t.child = Pa(t, null, r, n) : nc(e, t, r, n), t.child;
			case 11: return rc(e, t, t.type, t.pendingProps, n);
			case 7: return nc(e, t, t.pendingProps, n), t.child;
			case 8: return nc(e, t, t.pendingProps.children, n), t.child;
			case 12: return nc(e, t, t.pendingProps.children, n), t.child;
			case 10: return r = t.pendingProps, qi(t, t.type, r.value), nc(e, t, r.children, n), t.child;
			case 9: return i = t.type._context, r = t.pendingProps.children, $i(t), i = ea(i), r = r(i), t.flags |= 1, nc(e, t, r, n), t.child;
			case 14: return ic(e, t, t.type, t.pendingProps, n);
			case 15: return ac(e, t, t.type, t.pendingProps, n);
			case 19: return Ec(e, t, n);
			case 31: return dc(e, t, n);
			case 22: return oc(e, t, n, t.pendingProps);
			case 24: return $i(t), r = ea(oa), e === null ? (i = _a(), i === null && (i = q, o = sa(), i.pooledCache = o, o.refCount++, o !== null && (i.pooledCacheLanes |= n), i = o), t.memoizedState = {
				parent: r,
				cache: i
			}, La(t), qi(t, oa, i)) : ((e.lanes & n) !== 0 && (Ra(e, t), Ga(t, null, null, n), Wa()), i = e.memoizedState, o = t.memoizedState, i.parent === r ? (r = o.cache, qi(t, oa, r), r !== i.cache && Xi(t, [oa], n, !0)) : (i = {
				parent: r,
				cache: r
			}, t.memoizedState = i, t.lanes === 0 && (t.memoizedState = t.updateQueue.baseState = i), qi(t, oa, r))), nc(e, t, t.pendingProps.children, n), t.child;
			case 29: throw t.pendingProps;
		}
		throw Error(a(156, t.tag));
	}
	function jc(e) {
		e.flags |= 4;
	}
	function Mc(e, t, n, r, i) {
		if ((t = !!(e.mode & 32)) && (t = !1), t) {
			if (e.flags |= 16777216, (i & 335544128) === i) {
				if (e.stateNode.complete) e.flags |= 8192;
				else if (wu()) e.flags |= 8192;
				else throw Ta = xa, ya;
			}
		} else e.flags &= -16777217;
	}
	function Nc(e, t) {
		if (t.type !== "stylesheet" || t.state.loading & 4) e.flags &= -16777217;
		else if (e.flags |= 16777216, !Wf(t)) {
			if (wu()) e.flags |= 8192;
			else throw Ta = xa, ya;
		}
	}
	function Pc(e, t) {
		t !== null && (e.flags |= 4), e.flags & 16384 && (t = e.tag === 22 ? 536870912 : Ye(), e.lanes |= t, Yl |= t);
	}
	function Fc(e, t) {
		if (!B) switch (e.tailMode) {
			case "hidden":
				t = e.tail;
				for (var n = null; t !== null;) t.alternate !== null && (n = t), t = t.sibling;
				n === null ? e.tail = null : n.sibling = null;
				break;
			case "collapsed":
				n = e.tail;
				for (var r = null; n !== null;) n.alternate !== null && (r = n), n = n.sibling;
				r === null ? t || e.tail === null ? e.tail = null : e.tail.sibling = null : r.sibling = null;
		}
	}
	function Ic(e) {
		var t = e.alternate !== null && e.alternate.child === e.child, n = 0, r = 0;
		if (t) for (var i = e.child; i !== null;) n |= i.lanes | i.childLanes, r |= i.subtreeFlags & 65011712, r |= i.flags & 65011712, i.return = e, i = i.sibling;
		else for (i = e.child; i !== null;) n |= i.lanes | i.childLanes, r |= i.subtreeFlags, r |= i.flags, i.return = e, i = i.sibling;
		return e.subtreeFlags |= r, e.childLanes = n, t;
	}
	function Lc(e, t, n) {
		var r = t.pendingProps;
		switch (ji(t), t.tag) {
			case 16:
			case 15:
			case 0:
			case 11:
			case 7:
			case 8:
			case 12:
			case 9:
			case 14: return Ic(t), null;
			case 1: return Ic(t), null;
			case 3: return n = t.stateNode, r = null, e !== null && (r = e.memoizedState.cache), t.memoizedState.cache !== r && (t.flags |= 2048), Ji(oa), de(), n.pendingContext && (n.context = n.pendingContext, n.pendingContext = null), (e === null || e.child === null) && (Bi(t) ? jc(t) : e === null || e.memoizedState.isDehydrated && !(t.flags & 256) || (t.flags |= 1024, Hi())), Ic(t), null;
			case 26:
				var i = t.type, o = t.memoizedState;
				return e === null ? (jc(t), o === null ? (Ic(t), Mc(t, i, null, r, n)) : (Ic(t), Nc(t, o))) : o ? o === e.memoizedState ? (Ic(t), t.flags &= -16777217) : (jc(t), Ic(t), Nc(t, o)) : (e = e.memoizedProps, e !== r && jc(t), Ic(t), Mc(t, i, e, r, n)), null;
			case 27:
				if (pe(t), n = ce.current, i = t.type, e !== null && t.stateNode != null) e.memoizedProps !== r && jc(t);
				else {
					if (!r) {
						if (t.stateNode === null) throw Error(a(166));
						return Ic(t), null;
					}
					e = L.current, Bi(t) ? Ri(t, e) : (e = ff(i, r, n), t.stateNode = e, jc(t));
				}
				return Ic(t), null;
			case 5:
				if (pe(t), i = t.type, e !== null && t.stateNode != null) e.memoizedProps !== r && jc(t);
				else {
					if (!r) {
						if (t.stateNode === null) throw Error(a(166));
						return Ic(t), null;
					}
					if (o = L.current, Bi(t)) Ri(t, o);
					else {
						var s = Bd(ce.current);
						switch (o) {
							case 1:
								o = s.createElementNS("http://www.w3.org/2000/svg", i);
								break;
							case 2:
								o = s.createElementNS("http://www.w3.org/1998/Math/MathML", i);
								break;
							default: switch (i) {
								case "svg":
									o = s.createElementNS("http://www.w3.org/2000/svg", i);
									break;
								case "math":
									o = s.createElementNS("http://www.w3.org/1998/Math/MathML", i);
									break;
								case "script":
									o = s.createElement("div"), o.innerHTML = "<script><\/script>", o = o.removeChild(o.firstChild);
									break;
								case "select":
									o = typeof r.is == "string" ? s.createElement("select", { is: r.is }) : s.createElement("select"), r.multiple ? o.multiple = !0 : r.size && (o.size = r.size);
									break;
								default: o = typeof r.is == "string" ? s.createElement(i, { is: r.is }) : s.createElement(i);
							}
						}
						o[st] = t, o[ct] = r;
						a: for (s = t.child; s !== null;) {
							if (s.tag === 5 || s.tag === 6) o.appendChild(s.stateNode);
							else if (s.tag !== 4 && s.tag !== 27 && s.child !== null) {
								s.child.return = s, s = s.child;
								continue;
							}
							if (s === t) break a;
							for (; s.sibling === null;) {
								if (s.return === null || s.return === t) break a;
								s = s.return;
							}
							s.sibling.return = s.return, s = s.sibling;
						}
						t.stateNode = o;
						a: switch (Pd(o, i, r), i) {
							case "button":
							case "input":
							case "select":
							case "textarea":
								r = !!r.autoFocus;
								break a;
							case "img":
								r = !0;
								break a;
							default: r = !1;
						}
						r && jc(t);
					}
				}
				return Ic(t), Mc(t, t.type, e === null ? null : e.memoizedProps, t.pendingProps, n), null;
			case 6:
				if (e && t.stateNode != null) e.memoizedProps !== r && jc(t);
				else {
					if (typeof r != "string" && t.stateNode === null) throw Error(a(166));
					if (e = ce.current, Bi(t)) {
						if (e = t.stateNode, n = t.memoizedProps, r = null, i = Ni, i !== null) switch (i.tag) {
							case 27:
							case 5: r = i.memoizedProps;
						}
						e[st] = t, e = !!(e.nodeValue === n || r !== null && !0 === r.suppressHydrationWarning || Md(e.nodeValue, n)), e || Li(t, !0);
					} else e = Bd(e).createTextNode(r), e[st] = t, t.stateNode = e;
				}
				return Ic(t), null;
			case 31:
				if (n = t.memoizedState, e === null || e.memoizedState !== null) {
					if (r = Bi(t), n !== null) {
						if (e === null) {
							if (!r) throw Error(a(318));
							if (e = t.memoizedState, e = e === null ? null : e.dehydrated, !e) throw Error(a(557));
							e[st] = t;
						} else Vi(), !(t.flags & 128) && (t.memoizedState = null), t.flags |= 4;
						Ic(t), e = !1;
					} else n = Hi(), e !== null && e.memoizedState !== null && (e.memoizedState.hydrationErrors = n), e = !0;
					if (!e) return t.flags & 256 ? (ao(t), t) : (ao(t), null);
					if (t.flags & 128) throw Error(a(558));
				}
				return Ic(t), null;
			case 13:
				if (r = t.memoizedState, e === null || e.memoizedState !== null && e.memoizedState.dehydrated !== null) {
					if (i = Bi(t), r !== null && r.dehydrated !== null) {
						if (e === null) {
							if (!i) throw Error(a(318));
							if (i = t.memoizedState, i = i === null ? null : i.dehydrated, !i) throw Error(a(317));
							i[st] = t;
						} else Vi(), !(t.flags & 128) && (t.memoizedState = null), t.flags |= 4;
						Ic(t), i = !1;
					} else i = Hi(), e !== null && e.memoizedState !== null && (e.memoizedState.hydrationErrors = i), i = !0;
					if (!i) return t.flags & 256 ? (ao(t), t) : (ao(t), null);
				}
				return ao(t), t.flags & 128 ? (t.lanes = n, t) : (n = r !== null, e = e !== null && e.memoizedState !== null, n && (r = t.child, i = null, r.alternate !== null && r.alternate.memoizedState !== null && r.alternate.memoizedState.cachePool !== null && (i = r.alternate.memoizedState.cachePool.pool), o = null, r.memoizedState !== null && r.memoizedState.cachePool !== null && (o = r.memoizedState.cachePool.pool), o !== i && (r.flags |= 2048)), n !== e && n && (t.child.flags |= 8192), Pc(t, t.updateQueue), Ic(t), null);
			case 4: return de(), e === null && Sd(t.stateNode.containerInfo), Ic(t), null;
			case 10: return Ji(t.type), Ic(t), null;
			case 19:
				if (F(oo), r = t.memoizedState, r === null) return Ic(t), null;
				if (i = !!(t.flags & 128), o = r.rendering, o === null) {
					if (i) Fc(r, !1);
					else {
						if (Wl !== 0 || e !== null && e.flags & 128) for (e = t.child; e !== null;) {
							if (o = so(e), o !== null) {
								for (t.flags |= 128, Fc(r, !1), e = o.updateQueue, t.updateQueue = e, Pc(t, e), t.subtreeFlags = 0, e = n, n = t.child; n !== null;) di(n, e), n = n.sibling;
								return I(oo, oo.current & 1 | 2), B && Oi(t, r.treeForkCount), t.child;
							}
							e = e.sibling;
						}
						r.tail !== null && Ee() > tu && (t.flags |= 128, i = !0, Fc(r, !1), t.lanes = 4194304);
					}
				} else {
					if (!i) {
						if (e = so(o), e !== null) {
							if (t.flags |= 128, i = !0, e = e.updateQueue, t.updateQueue = e, Pc(t, e), Fc(r, !0), r.tail === null && r.tailMode === "hidden" && !o.alternate && !B) return Ic(t), null;
						} else 2 * Ee() - r.renderingStartTime > tu && n !== 536870912 && (t.flags |= 128, i = !0, Fc(r, !1), t.lanes = 4194304);
					}
					r.isBackwards ? (o.sibling = t.child, t.child = o) : (e = r.last, e === null ? t.child = o : e.sibling = o, r.last = o);
				}
				return r.tail === null ? (Ic(t), null) : (e = r.tail, r.rendering = e, r.tail = e.sibling, r.renderingStartTime = Ee(), e.sibling = null, n = oo.current, I(oo, i ? n & 1 | 2 : n & 1), B && Oi(t, r.treeForkCount), e);
			case 22:
			case 23: return ao(t), Qa(), r = t.memoizedState !== null, e === null ? r && (t.flags |= 8192) : e.memoizedState !== null !== r && (t.flags |= 8192), r ? n & 536870912 && !(t.flags & 128) && (Ic(t), t.subtreeFlags & 6 && (t.flags |= 8192)) : Ic(t), n = t.updateQueue, n !== null && Pc(t, n.retryQueue), n = null, e !== null && e.memoizedState !== null && e.memoizedState.cachePool !== null && (n = e.memoizedState.cachePool.pool), r = null, t.memoizedState !== null && t.memoizedState.cachePool !== null && (r = t.memoizedState.cachePool.pool), r !== n && (t.flags |= 2048), e !== null && F(V), null;
			case 24: return n = null, e !== null && (n = e.memoizedState.cache), t.memoizedState.cache !== n && (t.flags |= 2048), Ji(oa), Ic(t), null;
			case 25: return null;
			case 30: return null;
		}
		throw Error(a(156, t.tag));
	}
	function Rc(e, t) {
		switch (ji(t), t.tag) {
			case 1: return e = t.flags, e & 65536 ? (t.flags = e & -65537 | 128, t) : null;
			case 3: return Ji(oa), de(), e = t.flags, e & 65536 && !(e & 128) ? (t.flags = e & -65537 | 128, t) : null;
			case 26:
			case 27:
			case 5: return pe(t), null;
			case 31:
				if (t.memoizedState !== null) {
					if (ao(t), t.alternate === null) throw Error(a(340));
					Vi();
				}
				return e = t.flags, e & 65536 ? (t.flags = e & -65537 | 128, t) : null;
			case 13:
				if (ao(t), e = t.memoizedState, e !== null && e.dehydrated !== null) {
					if (t.alternate === null) throw Error(a(340));
					Vi();
				}
				return e = t.flags, e & 65536 ? (t.flags = e & -65537 | 128, t) : null;
			case 19: return F(oo), null;
			case 4: return de(), null;
			case 10: return Ji(t.type), null;
			case 22:
			case 23: return ao(t), Qa(), e !== null && F(V), e = t.flags, e & 65536 ? (t.flags = e & -65537 | 128, t) : null;
			case 24: return Ji(oa), null;
			case 25: return null;
			default: return null;
		}
	}
	function zc(e, t) {
		switch (ji(t), t.tag) {
			case 3:
				Ji(oa), de();
				break;
			case 26:
			case 27:
			case 5:
				pe(t);
				break;
			case 4:
				de();
				break;
			case 31:
				t.memoizedState !== null && ao(t);
				break;
			case 13:
				ao(t);
				break;
			case 19:
				F(oo);
				break;
			case 10:
				Ji(t.type);
				break;
			case 22:
			case 23:
				ao(t), Qa(), e !== null && F(V);
				break;
			case 24: Ji(oa);
		}
	}
	function Bc(e, t) {
		try {
			var n = t.updateQueue, r = n === null ? null : n.lastEffect;
			if (r !== null) {
				var i = r.next;
				n = i;
				do {
					if ((n.tag & e) === e) {
						r = void 0;
						var a = n.create, o = n.inst;
						r = a(), o.destroy = r;
					}
					n = n.next;
				} while (n !== i);
			}
		} catch (e) {
			Z(t, t.return, e);
		}
	}
	function Vc(e, t, n) {
		try {
			var r = t.updateQueue, i = r === null ? null : r.lastEffect;
			if (i !== null) {
				var a = i.next;
				r = a;
				do {
					if ((r.tag & e) === e) {
						var o = r.inst, s = o.destroy;
						if (s !== void 0) {
							o.destroy = void 0, i = t;
							var c = n, l = s;
							try {
								l();
							} catch (e) {
								Z(i, c, e);
							}
						}
					}
					r = r.next;
				} while (r !== a);
			}
		} catch (e) {
			Z(t, t.return, e);
		}
	}
	function Hc(e) {
		var t = e.updateQueue;
		if (t !== null) {
			var n = e.stateNode;
			try {
				qa(t, n);
			} catch (t) {
				Z(e, e.return, t);
			}
		}
	}
	function Uc(e, t, n) {
		n.props = Ws(e.type, e.memoizedProps), n.state = e.memoizedState;
		try {
			n.componentWillUnmount();
		} catch (n) {
			Z(e, t, n);
		}
	}
	function Wc(e, t) {
		try {
			var n = e.ref;
			if (n !== null) {
				switch (e.tag) {
					case 26:
					case 27:
					case 5:
						var r = e.stateNode;
						break;
					case 30:
						r = e.stateNode;
						break;
					default: r = e.stateNode;
				}
				typeof n == "function" ? e.refCleanup = n(r) : n.current = r;
			}
		} catch (n) {
			Z(e, t, n);
		}
	}
	function Gc(e, t) {
		var n = e.ref, r = e.refCleanup;
		if (n !== null) {
			if (typeof r == "function") try {
				r();
			} catch (n) {
				Z(e, t, n);
			} finally {
				e.refCleanup = null, e = e.alternate, e != null && (e.refCleanup = null);
			}
			else if (typeof n == "function") try {
				n(null);
			} catch (n) {
				Z(e, t, n);
			}
			else n.current = null;
		}
	}
	function Kc(e) {
		var t = e.type, n = e.memoizedProps, r = e.stateNode;
		try {
			a: switch (t) {
				case "button":
				case "input":
				case "select":
				case "textarea":
					n.autoFocus && r.focus();
					break a;
				case "img": n.src ? r.src = n.src : n.srcSet && (r.srcset = n.srcSet);
			}
		} catch (t) {
			Z(e, e.return, t);
		}
	}
	function qc(e, t, n) {
		try {
			var r = e.stateNode;
			Fd(r, e.type, n, t), r[ct] = t;
		} catch (t) {
			Z(e, e.return, t);
		}
	}
	function Jc(e) {
		return e.tag === 5 || e.tag === 3 || e.tag === 26 || e.tag === 27 && Zd(e.type) || e.tag === 4;
	}
	function Yc(e) {
		a: for (;;) {
			for (; e.sibling === null;) {
				if (e.return === null || Jc(e.return)) return null;
				e = e.return;
			}
			for (e.sibling.return = e.return, e = e.sibling; e.tag !== 5 && e.tag !== 6 && e.tag !== 18;) {
				if (e.tag === 27 && Zd(e.type) || e.flags & 2 || e.child === null || e.tag === 4) continue a;
				e.child.return = e, e = e.child;
			}
			if (!(e.flags & 2)) return e.stateNode;
		}
	}
	function Xc(e, t, n) {
		var r = e.tag;
		if (r === 5 || r === 6) e = e.stateNode, t ? (n.nodeType === 9 ? n.body : n.nodeName === "HTML" ? n.ownerDocument.body : n).insertBefore(e, t) : (t = n.nodeType === 9 ? n.body : n.nodeName === "HTML" ? n.ownerDocument.body : n, t.appendChild(e), n = n._reactRootContainer, n != null || t.onclick !== null || (t.onclick = en));
		else if (r !== 4 && (r === 27 && Zd(e.type) && (n = e.stateNode, t = null), e = e.child, e !== null)) for (Xc(e, t, n), e = e.sibling; e !== null;) Xc(e, t, n), e = e.sibling;
	}
	function Zc(e, t, n) {
		var r = e.tag;
		if (r === 5 || r === 6) e = e.stateNode, t ? n.insertBefore(e, t) : n.appendChild(e);
		else if (r !== 4 && (r === 27 && Zd(e.type) && (n = e.stateNode), e = e.child, e !== null)) for (Zc(e, t, n), e = e.sibling; e !== null;) Zc(e, t, n), e = e.sibling;
	}
	function Qc(e) {
		var t = e.stateNode, n = e.memoizedProps;
		try {
			for (var r = e.type, i = t.attributes; i.length;) t.removeAttributeNode(i[0]);
			Pd(t, r, n), t[st] = e, t[ct] = n;
		} catch (t) {
			Z(e, e.return, t);
		}
	}
	var $c = !1, el = !1, tl = !1, nl = typeof WeakSet == "function" ? WeakSet : Set, rl = null;
	function il(e, t) {
		if (e = e.containerInfo, Rd = sp, e = Dr(e), Or(e)) {
			if ("selectionStart" in e) var n = {
				start: e.selectionStart,
				end: e.selectionEnd
			};
			else a: {
				n = (n = e.ownerDocument) && n.defaultView || window;
				var r = n.getSelection && n.getSelection();
				if (r && r.rangeCount !== 0) {
					n = r.anchorNode;
					var i = r.anchorOffset, o = r.focusNode;
					r = r.focusOffset;
					try {
						n.nodeType, o.nodeType;
					} catch {
						n = null;
						break a;
					}
					var s = 0, c = -1, l = -1, u = 0, d = 0, f = e, p = null;
					b: for (;;) {
						for (var m; f !== n || i !== 0 && f.nodeType !== 3 || (c = s + i), f !== o || r !== 0 && f.nodeType !== 3 || (l = s + r), f.nodeType === 3 && (s += f.nodeValue.length), (m = f.firstChild) !== null;) p = f, f = m;
						for (;;) {
							if (f === e) break b;
							if (p === n && ++u === i && (c = s), p === o && ++d === r && (l = s), (m = f.nextSibling) !== null) break;
							f = p, p = f.parentNode;
						}
						f = m;
					}
					n = c === -1 || l === -1 ? null : {
						start: c,
						end: l
					};
				} else n = null;
			}
			n ||= {
				start: 0,
				end: 0
			};
		} else n = null;
		for (zd = {
			focusedElem: e,
			selectionRange: n
		}, sp = !1, rl = t; rl !== null;) if (t = rl, e = t.child, t.subtreeFlags & 1028 && e !== null) e.return = t, rl = e;
		else for (; rl !== null;) {
			switch (t = rl, o = t.alternate, e = t.flags, t.tag) {
				case 0:
					if (e & 4 && (e = t.updateQueue, e = e === null ? null : e.events, e !== null)) for (n = 0; n < e.length; n++) i = e[n], i.ref.impl = i.nextImpl;
					break;
				case 11:
				case 15: break;
				case 1:
					if (e & 1024 && o !== null) {
						e = void 0, n = t, i = o.memoizedProps, o = o.memoizedState, r = n.stateNode;
						try {
							var h = Ws(n.type, i);
							e = r.getSnapshotBeforeUpdate(h, o), r.__reactInternalSnapshotBeforeUpdate = e;
						} catch (e) {
							Z(n, n.return, e);
						}
					}
					break;
				case 3:
					if (e & 1024) {
						if (e = t.stateNode.containerInfo, n = e.nodeType, n === 9) ef(e);
						else if (n === 1) switch (e.nodeName) {
							case "HEAD":
							case "HTML":
							case "BODY":
								ef(e);
								break;
							default: e.textContent = "";
						}
					}
					break;
				case 5:
				case 26:
				case 27:
				case 6:
				case 4:
				case 17: break;
				default: if (e & 1024) throw Error(a(163));
			}
			if (e = t.sibling, e !== null) {
				e.return = t.return, rl = e;
				break;
			}
			rl = t.return;
		}
	}
	function al(e, t, n) {
		var r = n.flags;
		switch (n.tag) {
			case 0:
			case 11:
			case 15:
				bl(e, n), r & 4 && Bc(5, n);
				break;
			case 1:
				if (bl(e, n), r & 4) {
					if (e = n.stateNode, t === null) try {
						e.componentDidMount();
					} catch (e) {
						Z(n, n.return, e);
					}
					else {
						var i = Ws(n.type, t.memoizedProps);
						t = t.memoizedState;
						try {
							e.componentDidUpdate(i, t, e.__reactInternalSnapshotBeforeUpdate);
						} catch (e) {
							Z(n, n.return, e);
						}
					}
				}
				r & 64 && Hc(n), r & 512 && Wc(n, n.return);
				break;
			case 3:
				if (bl(e, n), r & 64 && (e = n.updateQueue, e !== null)) {
					if (t = null, n.child !== null) switch (n.child.tag) {
						case 27:
						case 5:
							t = n.child.stateNode;
							break;
						case 1: t = n.child.stateNode;
					}
					try {
						qa(e, t);
					} catch (e) {
						Z(n, n.return, e);
					}
				}
				break;
			case 27: t === null && r & 4 && Qc(n);
			case 26:
			case 5:
				bl(e, n), t === null && r & 4 && Kc(n), r & 512 && Wc(n, n.return);
				break;
			case 12:
				bl(e, n);
				break;
			case 31:
				bl(e, n), r & 4 && dl(e, n);
				break;
			case 13:
				bl(e, n), r & 4 && fl(e, n), r & 64 && (e = n.memoizedState, e !== null && (e = e.dehydrated, e !== null && (n = Ju.bind(null, n), sf(e, n))));
				break;
			case 22:
				if (r = n.memoizedState !== null || $c, !r) {
					t = t !== null && t.memoizedState !== null || el, i = $c;
					var a = el;
					$c = r, (el = t) && !a ? Sl(e, n, !!(n.subtreeFlags & 8772)) : bl(e, n), $c = i, el = a;
				}
				break;
			case 30: break;
			default: bl(e, n);
		}
	}
	function ol(e) {
		var t = e.alternate;
		t !== null && (e.alternate = null, ol(t)), e.child = null, e.deletions = null, e.sibling = null, e.tag === 5 && (t = e.stateNode, t !== null && ht(t)), e.stateNode = null, e.return = null, e.dependencies = null, e.memoizedProps = null, e.memoizedState = null, e.pendingProps = null, e.stateNode = null, e.updateQueue = null;
	}
	var sl = null, cl = !1;
	function ll(e, t, n) {
		for (n = n.child; n !== null;) ul(e, t, n), n = n.sibling;
	}
	function ul(e, t, n) {
		if (Ie && typeof Ie.onCommitFiberUnmount == "function") try {
			Ie.onCommitFiberUnmount(Fe, n);
		} catch {}
		switch (n.tag) {
			case 26:
				el || Gc(n, t), ll(e, t, n), n.memoizedState ? n.memoizedState.count-- : n.stateNode && (n = n.stateNode, n.parentNode.removeChild(n));
				break;
			case 27:
				el || Gc(n, t);
				var r = sl, i = cl;
				Zd(n.type) && (sl = n.stateNode, cl = !1), ll(e, t, n), pf(n.stateNode), sl = r, cl = i;
				break;
			case 5: el || Gc(n, t);
			case 6:
				if (r = sl, i = cl, sl = null, ll(e, t, n), sl = r, cl = i, sl !== null) {
					if (cl) try {
						(sl.nodeType === 9 ? sl.body : sl.nodeName === "HTML" ? sl.ownerDocument.body : sl).removeChild(n.stateNode);
					} catch (e) {
						Z(n, t, e);
					}
					else try {
						sl.removeChild(n.stateNode);
					} catch (e) {
						Z(n, t, e);
					}
				}
				break;
			case 18:
				sl !== null && (cl ? (e = sl, Qd(e.nodeType === 9 ? e.body : e.nodeName === "HTML" ? e.ownerDocument.body : e, n.stateNode), Np(e)) : Qd(sl, n.stateNode));
				break;
			case 4:
				r = sl, i = cl, sl = n.stateNode.containerInfo, cl = !0, ll(e, t, n), sl = r, cl = i;
				break;
			case 0:
			case 11:
			case 14:
			case 15:
				Vc(2, n, t), el || Vc(4, n, t), ll(e, t, n);
				break;
			case 1:
				el || (Gc(n, t), r = n.stateNode, typeof r.componentWillUnmount == "function" && Uc(n, t, r)), ll(e, t, n);
				break;
			case 21:
				ll(e, t, n);
				break;
			case 22:
				el = (r = el) || n.memoizedState !== null, ll(e, t, n), el = r;
				break;
			default: ll(e, t, n);
		}
	}
	function dl(e, t) {
		if (t.memoizedState === null && (e = t.alternate, e !== null && (e = e.memoizedState, e !== null))) {
			e = e.dehydrated;
			try {
				Np(e);
			} catch (e) {
				Z(t, t.return, e);
			}
		}
	}
	function fl(e, t) {
		if (t.memoizedState === null && (e = t.alternate, e !== null && (e = e.memoizedState, e !== null && (e = e.dehydrated, e !== null)))) try {
			Np(e);
		} catch (e) {
			Z(t, t.return, e);
		}
	}
	function pl(e) {
		switch (e.tag) {
			case 31:
			case 13:
			case 19:
				var t = e.stateNode;
				return t === null && (t = e.stateNode = new nl()), t;
			case 22: return e = e.stateNode, t = e._retryCache, t === null && (t = e._retryCache = new nl()), t;
			default: throw Error(a(435, e.tag));
		}
	}
	function ml(e, t) {
		var n = pl(e);
		t.forEach(function(t) {
			if (!n.has(t)) {
				n.add(t);
				var r = Yu.bind(null, e, t);
				t.then(r, r);
			}
		});
	}
	function hl(e, t) {
		var n = t.deletions;
		if (n !== null) for (var r = 0; r < n.length; r++) {
			var i = n[r], o = e, s = t, c = s;
			a: for (; c !== null;) {
				switch (c.tag) {
					case 27:
						if (Zd(c.type)) {
							sl = c.stateNode, cl = !1;
							break a;
						}
						break;
					case 5:
						sl = c.stateNode, cl = !1;
						break a;
					case 3:
					case 4:
						sl = c.stateNode.containerInfo, cl = !0;
						break a;
				}
				c = c.return;
			}
			if (sl === null) throw Error(a(160));
			ul(o, s, i), sl = null, cl = !1, o = i.alternate, o !== null && (o.return = null), i.return = null;
		}
		if (t.subtreeFlags & 13886) for (t = t.child; t !== null;) _l(t, e), t = t.sibling;
	}
	var gl = null;
	function _l(e, t) {
		var n = e.alternate, r = e.flags;
		switch (e.tag) {
			case 0:
			case 11:
			case 14:
			case 15:
				hl(t, e), vl(e), r & 4 && (Vc(3, e, e.return), Bc(3, e), Vc(5, e, e.return));
				break;
			case 1:
				hl(t, e), vl(e), r & 512 && (el || n === null || Gc(n, n.return)), r & 64 && $c && (e = e.updateQueue, e !== null && (r = e.callbacks, r !== null && (n = e.shared.hiddenCallbacks, e.shared.hiddenCallbacks = n === null ? r : n.concat(r))));
				break;
			case 26:
				var i = gl;
				if (hl(t, e), vl(e), r & 512 && (el || n === null || Gc(n, n.return)), r & 4) {
					var o = n === null ? null : n.memoizedState;
					if (r = e.memoizedState, n === null) {
						if (r === null) {
							if (e.stateNode === null) {
								a: {
									r = e.type, n = e.memoizedProps, i = i.ownerDocument || i;
									b: switch (r) {
										case "title":
											o = i.getElementsByTagName("title")[0], (!o || o[mt] || o[st] || o.namespaceURI === "http://www.w3.org/2000/svg" || o.hasAttribute("itemprop")) && (o = i.createElement(r), i.head.insertBefore(o, i.querySelector("head > title"))), Pd(o, r, n), o[st] = e, bt(o), r = o;
											break a;
										case "link":
											var s = Vf("link", "href", i).get(r + (n.href || ""));
											if (s) {
												for (var c = 0; c < s.length; c++) if (o = s[c], o.getAttribute("href") === (n.href == null || n.href === "" ? null : n.href) && o.getAttribute("rel") === (n.rel == null ? null : n.rel) && o.getAttribute("title") === (n.title == null ? null : n.title) && o.getAttribute("crossorigin") === (n.crossOrigin == null ? null : n.crossOrigin)) {
													s.splice(c, 1);
													break b;
												}
											}
											o = i.createElement(r), Pd(o, r, n), i.head.appendChild(o);
											break;
										case "meta":
											if (s = Vf("meta", "content", i).get(r + (n.content || ""))) {
												for (c = 0; c < s.length; c++) if (o = s[c], o.getAttribute("content") === (n.content == null ? null : "" + n.content) && o.getAttribute("name") === (n.name == null ? null : n.name) && o.getAttribute("property") === (n.property == null ? null : n.property) && o.getAttribute("http-equiv") === (n.httpEquiv == null ? null : n.httpEquiv) && o.getAttribute("charset") === (n.charSet == null ? null : n.charSet)) {
													s.splice(c, 1);
													break b;
												}
											}
											o = i.createElement(r), Pd(o, r, n), i.head.appendChild(o);
											break;
										default: throw Error(a(468, r));
									}
									o[st] = e, bt(o), r = o;
								}
								e.stateNode = r;
							} else Hf(i, e.type, e.stateNode);
						} else e.stateNode = If(i, r, e.memoizedProps);
					} else o === r ? r === null && e.stateNode !== null && qc(e, e.memoizedProps, n.memoizedProps) : (o === null ? n.stateNode !== null && (n = n.stateNode, n.parentNode.removeChild(n)) : o.count--, r === null ? Hf(i, e.type, e.stateNode) : If(i, r, e.memoizedProps));
				}
				break;
			case 27:
				hl(t, e), vl(e), r & 512 && (el || n === null || Gc(n, n.return)), n !== null && r & 4 && qc(e, e.memoizedProps, n.memoizedProps);
				break;
			case 5:
				if (hl(t, e), vl(e), r & 512 && (el || n === null || Gc(n, n.return)), e.flags & 32) {
					i = e.stateNode;
					try {
						Kt(i, "");
					} catch (t) {
						Z(e, e.return, t);
					}
				}
				r & 4 && e.stateNode != null && (i = e.memoizedProps, qc(e, i, n === null ? i : n.memoizedProps)), r & 1024 && (tl = !0);
				break;
			case 6:
				if (hl(t, e), vl(e), r & 4) {
					if (e.stateNode === null) throw Error(a(162));
					r = e.memoizedProps, n = e.stateNode;
					try {
						n.nodeValue = r;
					} catch (t) {
						Z(e, e.return, t);
					}
				}
				break;
			case 3:
				if (Bf = null, i = gl, gl = gf(t.containerInfo), hl(t, e), gl = i, vl(e), r & 4 && n !== null && n.memoizedState.isDehydrated) try {
					Np(t.containerInfo);
				} catch (t) {
					Z(e, e.return, t);
				}
				tl && (tl = !1, yl(e));
				break;
			case 4:
				r = gl, gl = gf(e.stateNode.containerInfo), hl(t, e), vl(e), gl = r;
				break;
			case 12:
				hl(t, e), vl(e);
				break;
			case 31:
				hl(t, e), vl(e), r & 4 && (r = e.updateQueue, r !== null && (e.updateQueue = null, ml(e, r)));
				break;
			case 13:
				hl(t, e), vl(e), e.child.flags & 8192 && e.memoizedState !== null != (n !== null && n.memoizedState !== null) && ($l = Ee()), r & 4 && (r = e.updateQueue, r !== null && (e.updateQueue = null, ml(e, r)));
				break;
			case 22:
				i = e.memoizedState !== null;
				var l = n !== null && n.memoizedState !== null, u = $c, d = el;
				if ($c = u || i, el = d || l, hl(t, e), el = d, $c = u, vl(e), r & 8192) a: for (t = e.stateNode, t._visibility = i ? t._visibility & -2 : t._visibility | 1, i && (n === null || l || $c || el || xl(e)), n = null, t = e;;) {
					if (t.tag === 5 || t.tag === 26) {
						if (n === null) {
							l = n = t;
							try {
								if (o = l.stateNode, i) s = o.style, typeof s.setProperty == "function" ? s.setProperty("display", "none", "important") : s.display = "none";
								else {
									c = l.stateNode;
									var f = l.memoizedProps.style, p = f != null && f.hasOwnProperty("display") ? f.display : null;
									c.style.display = p == null || typeof p == "boolean" ? "" : ("" + p).trim();
								}
							} catch (e) {
								Z(l, l.return, e);
							}
						}
					} else if (t.tag === 6) {
						if (n === null) {
							l = t;
							try {
								l.stateNode.nodeValue = i ? "" : l.memoizedProps;
							} catch (e) {
								Z(l, l.return, e);
							}
						}
					} else if (t.tag === 18) {
						if (n === null) {
							l = t;
							try {
								var m = l.stateNode;
								i ? $d(m, !0) : $d(l.stateNode, !1);
							} catch (e) {
								Z(l, l.return, e);
							}
						}
					} else if ((t.tag !== 22 && t.tag !== 23 || t.memoizedState === null || t === e) && t.child !== null) {
						t.child.return = t, t = t.child;
						continue;
					}
					if (t === e) break a;
					for (; t.sibling === null;) {
						if (t.return === null || t.return === e) break a;
						n === t && (n = null), t = t.return;
					}
					n === t && (n = null), t.sibling.return = t.return, t = t.sibling;
				}
				r & 4 && (r = e.updateQueue, r !== null && (n = r.retryQueue, n !== null && (r.retryQueue = null, ml(e, n))));
				break;
			case 19:
				hl(t, e), vl(e), r & 4 && (r = e.updateQueue, r !== null && (e.updateQueue = null, ml(e, r)));
				break;
			case 30: break;
			case 21: break;
			default: hl(t, e), vl(e);
		}
	}
	function vl(e) {
		var t = e.flags;
		if (t & 2) {
			try {
				for (var n, r = e.return; r !== null;) {
					if (Jc(r)) {
						n = r;
						break;
					}
					r = r.return;
				}
				if (n == null) throw Error(a(160));
				switch (n.tag) {
					case 27:
						var i = n.stateNode;
						Zc(e, Yc(e), i);
						break;
					case 5:
						var o = n.stateNode;
						n.flags & 32 && (Kt(o, ""), n.flags &= -33), Zc(e, Yc(e), o);
						break;
					case 3:
					case 4:
						var s = n.stateNode.containerInfo;
						Xc(e, Yc(e), s);
						break;
					default: throw Error(a(161));
				}
			} catch (t) {
				Z(e, e.return, t);
			}
			e.flags &= -3;
		}
		t & 4096 && (e.flags &= -4097);
	}
	function yl(e) {
		if (e.subtreeFlags & 1024) for (e = e.child; e !== null;) {
			var t = e;
			yl(t), t.tag === 5 && t.flags & 1024 && t.stateNode.reset(), e = e.sibling;
		}
	}
	function bl(e, t) {
		if (t.subtreeFlags & 8772) for (t = t.child; t !== null;) al(e, t.alternate, t), t = t.sibling;
	}
	function xl(e) {
		for (e = e.child; e !== null;) {
			var t = e;
			switch (t.tag) {
				case 0:
				case 11:
				case 14:
				case 15:
					Vc(4, t, t.return), xl(t);
					break;
				case 1:
					Gc(t, t.return);
					var n = t.stateNode;
					typeof n.componentWillUnmount == "function" && Uc(t, t.return, n), xl(t);
					break;
				case 27: pf(t.stateNode);
				case 26:
				case 5:
					Gc(t, t.return), xl(t);
					break;
				case 22:
					t.memoizedState === null && xl(t);
					break;
				case 30:
					xl(t);
					break;
				default: xl(t);
			}
			e = e.sibling;
		}
	}
	function Sl(e, t, n) {
		for (n &&= !!(t.subtreeFlags & 8772), t = t.child; t !== null;) {
			var r = t.alternate, i = e, a = t, o = a.flags;
			switch (a.tag) {
				case 0:
				case 11:
				case 15:
					Sl(i, a, n), Bc(4, a);
					break;
				case 1:
					if (Sl(i, a, n), r = a, i = r.stateNode, typeof i.componentDidMount == "function") try {
						i.componentDidMount();
					} catch (e) {
						Z(r, r.return, e);
					}
					if (r = a, i = r.updateQueue, i !== null) {
						var s = r.stateNode;
						try {
							var c = i.shared.hiddenCallbacks;
							if (c !== null) for (i.shared.hiddenCallbacks = null, i = 0; i < c.length; i++) Ka(c[i], s);
						} catch (e) {
							Z(r, r.return, e);
						}
					}
					n && o & 64 && Hc(a), Wc(a, a.return);
					break;
				case 27: Qc(a);
				case 26:
				case 5:
					Sl(i, a, n), n && r === null && o & 4 && Kc(a), Wc(a, a.return);
					break;
				case 12:
					Sl(i, a, n);
					break;
				case 31:
					Sl(i, a, n), n && o & 4 && dl(i, a);
					break;
				case 13:
					Sl(i, a, n), n && o & 4 && fl(i, a);
					break;
				case 22:
					a.memoizedState === null && Sl(i, a, n), Wc(a, a.return);
					break;
				case 30: break;
				default: Sl(i, a, n);
			}
			t = t.sibling;
		}
	}
	function Cl(e, t) {
		var n = null;
		e !== null && e.memoizedState !== null && e.memoizedState.cachePool !== null && (n = e.memoizedState.cachePool.pool), e = null, t.memoizedState !== null && t.memoizedState.cachePool !== null && (e = t.memoizedState.cachePool.pool), e !== n && (e != null && e.refCount++, n != null && ca(n));
	}
	function wl(e, t) {
		e = null, t.alternate !== null && (e = t.alternate.memoizedState.cache), t = t.memoizedState.cache, t !== e && (t.refCount++, e != null && ca(e));
	}
	function Tl(e, t, n, r) {
		if (t.subtreeFlags & 10256) for (t = t.child; t !== null;) El(e, t, n, r), t = t.sibling;
	}
	function El(e, t, n, r) {
		var i = t.flags;
		switch (t.tag) {
			case 0:
			case 11:
			case 15:
				Tl(e, t, n, r), i & 2048 && Bc(9, t);
				break;
			case 1:
				Tl(e, t, n, r);
				break;
			case 3:
				Tl(e, t, n, r), i & 2048 && (e = null, t.alternate !== null && (e = t.alternate.memoizedState.cache), t = t.memoizedState.cache, t !== e && (t.refCount++, e != null && ca(e)));
				break;
			case 12:
				if (i & 2048) {
					Tl(e, t, n, r), e = t.stateNode;
					try {
						var a = t.memoizedProps, o = a.id, s = a.onPostCommit;
						typeof s == "function" && s(o, t.alternate === null ? "mount" : "update", e.passiveEffectDuration, -0);
					} catch (e) {
						Z(t, t.return, e);
					}
				} else Tl(e, t, n, r);
				break;
			case 31:
				Tl(e, t, n, r);
				break;
			case 13:
				Tl(e, t, n, r);
				break;
			case 23: break;
			case 22:
				a = t.stateNode, o = t.alternate, t.memoizedState === null ? a._visibility & 2 ? Tl(e, t, n, r) : (a._visibility |= 2, Dl(e, t, n, r, !!(t.subtreeFlags & 10256) || !1)) : a._visibility & 2 ? Tl(e, t, n, r) : Ol(e, t), i & 2048 && Cl(o, t);
				break;
			case 24:
				Tl(e, t, n, r), i & 2048 && wl(t.alternate, t);
				break;
			default: Tl(e, t, n, r);
		}
	}
	function Dl(e, t, n, r, i) {
		for (i &&= !!(t.subtreeFlags & 10256) || !1, t = t.child; t !== null;) {
			var a = e, o = t, s = n, c = r, l = o.flags;
			switch (o.tag) {
				case 0:
				case 11:
				case 15:
					Dl(a, o, s, c, i), Bc(8, o);
					break;
				case 23: break;
				case 22:
					var u = o.stateNode;
					o.memoizedState === null ? (u._visibility |= 2, Dl(a, o, s, c, i)) : u._visibility & 2 ? Dl(a, o, s, c, i) : Ol(a, o), i && l & 2048 && Cl(o.alternate, o);
					break;
				case 24:
					Dl(a, o, s, c, i), i && l & 2048 && wl(o.alternate, o);
					break;
				default: Dl(a, o, s, c, i);
			}
			t = t.sibling;
		}
	}
	function Ol(e, t) {
		if (t.subtreeFlags & 10256) for (t = t.child; t !== null;) {
			var n = e, r = t, i = r.flags;
			switch (r.tag) {
				case 22:
					Ol(n, r), i & 2048 && Cl(r.alternate, r);
					break;
				case 24:
					Ol(n, r), i & 2048 && wl(r.alternate, r);
					break;
				default: Ol(n, r);
			}
			t = t.sibling;
		}
	}
	var kl = 8192;
	function Al(e, t, n) {
		if (e.subtreeFlags & kl) for (e = e.child; e !== null;) jl(e, t, n), e = e.sibling;
	}
	function jl(e, t, n) {
		switch (e.tag) {
			case 26:
				Al(e, t, n), e.flags & kl && e.memoizedState !== null && Gf(n, gl, e.memoizedState, e.memoizedProps);
				break;
			case 5:
				Al(e, t, n);
				break;
			case 3:
			case 4:
				var r = gl;
				gl = gf(e.stateNode.containerInfo), Al(e, t, n), gl = r;
				break;
			case 22:
				e.memoizedState === null && (r = e.alternate, r !== null && r.memoizedState !== null ? (r = kl, kl = 16777216, Al(e, t, n), kl = r) : Al(e, t, n));
				break;
			default: Al(e, t, n);
		}
	}
	function Ml(e) {
		var t = e.alternate;
		if (t !== null && (e = t.child, e !== null)) {
			t.child = null;
			do
				t = e.sibling, e.sibling = null, e = t;
			while (e !== null);
		}
	}
	function Nl(e) {
		var t = e.deletions;
		if (e.flags & 16) {
			if (t !== null) for (var n = 0; n < t.length; n++) {
				var r = t[n];
				rl = r, Il(r, e);
			}
			Ml(e);
		}
		if (e.subtreeFlags & 10256) for (e = e.child; e !== null;) Pl(e), e = e.sibling;
	}
	function Pl(e) {
		switch (e.tag) {
			case 0:
			case 11:
			case 15:
				Nl(e), e.flags & 2048 && Vc(9, e, e.return);
				break;
			case 3:
				Nl(e);
				break;
			case 12:
				Nl(e);
				break;
			case 22:
				var t = e.stateNode;
				e.memoizedState !== null && t._visibility & 2 && (e.return === null || e.return.tag !== 13) ? (t._visibility &= -3, Fl(e)) : Nl(e);
				break;
			default: Nl(e);
		}
	}
	function Fl(e) {
		var t = e.deletions;
		if (e.flags & 16) {
			if (t !== null) for (var n = 0; n < t.length; n++) {
				var r = t[n];
				rl = r, Il(r, e);
			}
			Ml(e);
		}
		for (e = e.child; e !== null;) {
			switch (t = e, t.tag) {
				case 0:
				case 11:
				case 15:
					Vc(8, t, t.return), Fl(t);
					break;
				case 22:
					n = t.stateNode, n._visibility & 2 && (n._visibility &= -3, Fl(t));
					break;
				default: Fl(t);
			}
			e = e.sibling;
		}
	}
	function Il(e, t) {
		for (; rl !== null;) {
			var n = rl;
			switch (n.tag) {
				case 0:
				case 11:
				case 15:
					Vc(8, n, t);
					break;
				case 23:
				case 22:
					if (n.memoizedState !== null && n.memoizedState.cachePool !== null) {
						var r = n.memoizedState.cachePool.pool;
						r != null && r.refCount++;
					}
					break;
				case 24: ca(n.memoizedState.cache);
			}
			if (r = n.child, r !== null) r.return = n, rl = r;
			else a: for (n = e; rl !== null;) {
				r = rl;
				var i = r.sibling, a = r.return;
				if (ol(r), r === n) {
					rl = null;
					break a;
				}
				if (i !== null) {
					i.return = a, rl = i;
					break a;
				}
				rl = a;
			}
		}
	}
	var Ll = {
		getCacheForType: function(e) {
			var t = ea(oa), n = t.data.get(e);
			return n === void 0 && (n = e(), t.data.set(e, n)), n;
		},
		cacheSignal: function() {
			return ea(oa).controller.signal;
		}
	}, Rl = typeof WeakMap == "function" ? WeakMap : Map, K = 0, q = null, J = null, Y = 0, X = 0, zl = null, Bl = !1, Vl = !1, Hl = !1, Ul = 0, Wl = 0, Gl = 0, Kl = 0, ql = 0, Jl = 0, Yl = 0, Xl = null, Zl = null, Ql = !1, $l = 0, eu = 0, tu = Infinity, nu = null, ru = null, iu = 0, au = null, ou = null, su = 0, cu = 0, lu = null, uu = null, du = 0, fu = null;
	function pu() {
		return K & 2 && Y !== 0 ? Y & -Y : N.T === null ? it() : dd();
	}
	function mu() {
		if (Jl === 0) {
			if (!(Y & 536870912) || B) {
				var e = Ue;
				Ue <<= 1, !(Ue & 3932160) && (Ue = 262144), Jl = e;
			} else Jl = 536870912;
		}
		return e = $a.current, e !== null && (e.flags |= 32), Jl;
	}
	function hu(e, t, n) {
		(e === q && (X === 2 || X === 9) || e.cancelPendingCommit !== null) && (Su(e, 0), yu(e, Y, Jl, !1)), Ze(e, n), (!(K & 2) || e !== q) && (e === q && (!(K & 2) && (Kl |= n), Wl === 4 && yu(e, Y, Jl, !1)), rd(e));
	}
	function gu(e, t, n) {
		if (K & 6) throw Error(a(327));
		var r = !n && !(t & 127) && (t & e.expiredLanes) === 0 || qe(e, t), i = r ? Au(e, t) : Ou(e, t, !0), o = r;
		do {
			if (i === 0) {
				Vl && !r && yu(e, t, 0, !1);
				break;
			}
			if (n = e.current.alternate, o && !vu(n)) {
				i = Ou(e, t, !1), o = !1;
				continue;
			}
			if (i === 2) {
				if (o = t, e.errorRecoveryDisabledLanes & o) var s = 0;
				else s = e.pendingLanes & -536870913, s = s === 0 ? s & 536870912 ? 536870912 : 0 : s;
				if (s !== 0) {
					t = s;
					a: {
						var c = e;
						i = Xl;
						var l = c.current.memoizedState.isDehydrated;
						if (l && (Su(c, s).flags |= 256), s = Ou(c, s, !1), s !== 2) {
							if (Hl && !l) {
								c.errorRecoveryDisabledLanes |= o, Kl |= o, i = 4;
								break a;
							}
							o = Zl, Zl = i, o !== null && (Zl === null ? Zl = o : Zl.push.apply(Zl, o));
						}
						i = s;
					}
					if (o = !1, i !== 2) continue;
				}
			}
			if (i === 1) {
				Su(e, 0), yu(e, t, 0, !0);
				break;
			}
			a: {
				switch (r = e, o = i, o) {
					case 0:
					case 1: throw Error(a(345));
					case 4: if ((t & 4194048) !== t) break;
					case 6:
						yu(r, t, Jl, !Bl);
						break a;
					case 2:
						Zl = null;
						break;
					case 3:
					case 5: break;
					default: throw Error(a(329));
				}
				if ((t & 62914560) === t && (i = $l + 300 - Ee(), 10 < i)) {
					if (yu(r, t, Jl, !Bl), Ke(r, 0, !0) !== 0) break a;
					su = t, r.timeoutHandle = Kd(_u.bind(null, r, n, Zl, nu, Ql, t, Jl, Kl, Yl, Bl, o, "Throttled", -0, 0), i);
					break a;
				}
				_u(r, n, Zl, nu, Ql, t, Jl, Kl, Yl, Bl, o, null, -0, 0);
			}
			break;
		} while (1);
		rd(e);
	}
	function _u(e, t, n, r, i, a, o, s, c, l, u, d, f, p) {
		if (e.timeoutHandle = -1, d = t.subtreeFlags, d & 8192 || (d & 16785408) == 16785408) {
			d = {
				stylesheets: null,
				count: 0,
				imgCount: 0,
				imgBytes: 0,
				suspenseyImages: [],
				waitingForImages: !0,
				waitingForViewTransition: !1,
				unsuspend: en
			}, jl(t, a, d);
			var m = (a & 62914560) === a ? $l - Ee() : (a & 4194048) === a ? eu - Ee() : 0;
			if (m = qf(d, m), m !== null) {
				su = a, e.cancelPendingCommit = m(Lu.bind(null, e, t, a, n, r, i, o, s, c, u, d, null, f, p)), yu(e, a, o, !l);
				return;
			}
		}
		Lu(e, t, a, n, r, i, o, s, c);
	}
	function vu(e) {
		for (var t = e;;) {
			var n = t.tag;
			if ((n === 0 || n === 11 || n === 15) && t.flags & 16384 && (n = t.updateQueue, n !== null && (n = n.stores, n !== null))) for (var r = 0; r < n.length; r++) {
				var i = n[r], a = i.getSnapshot;
				i = i.value;
				try {
					if (!Sr(a(), i)) return !1;
				} catch {
					return !1;
				}
			}
			if (n = t.child, t.subtreeFlags & 16384 && n !== null) n.return = t, t = n;
			else {
				if (t === e) break;
				for (; t.sibling === null;) {
					if (t.return === null || t.return === e) return !0;
					t = t.return;
				}
				t.sibling.return = t.return, t = t.sibling;
			}
		}
		return !0;
	}
	function yu(e, t, n, r) {
		t &= ~ql, t &= ~Kl, e.suspendedLanes |= t, e.pingedLanes &= ~t, r && (e.warmLanes |= t), r = e.expirationTimes;
		for (var i = t; 0 < i;) {
			var a = 31 - Re(i), o = 1 << a;
			r[a] = -1, i &= ~o;
		}
		n !== 0 && $e(e, n, t);
	}
	function bu() {
		return K & 6 ? !0 : (id(0, !1), !1);
	}
	function xu() {
		if (J !== null) {
			if (X === 0) var e = J.return;
			else e = J, Ki = Gi = null, Eo(e), Oa = null, ka = 0, e = J;
			for (; e !== null;) zc(e.alternate, e), e = e.return;
			J = null;
		}
	}
	function Su(e, t) {
		var n = e.timeoutHandle;
		n !== -1 && (e.timeoutHandle = -1, qd(n)), n = e.cancelPendingCommit, n !== null && (e.cancelPendingCommit = null, n()), su = 0, xu(), q = e, J = n = ui(e.current, null), Y = t, X = 0, zl = null, Bl = !1, Vl = qe(e, t), Hl = !1, Yl = Jl = ql = Kl = Gl = Wl = 0, Zl = Xl = null, Ql = !1, t & 8 && (t |= t & 32);
		var r = e.entangledLanes;
		if (r !== 0) for (e = e.entanglements, r &= t; 0 < r;) {
			var i = 31 - Re(r), a = 1 << i;
			t |= e[i], r &= ~a;
		}
		return Ul = t, ei(), n;
	}
	function Cu(e, t) {
		W = null, N.H = Is, t === U || t === ba ? (t = Ea(), X = 3) : t === ya ? (t = Ea(), X = 4) : X = t === ec ? 8 : typeof t == "object" && t && typeof t.then == "function" ? 6 : 1, zl = t, J === null && (Wl = 1, Js(e, vi(t, e.current)));
	}
	function wu() {
		var e = $a.current;
		return e === null ? !0 : (Y & 4194048) === Y ? eo === null : (Y & 62914560) === Y || Y & 536870912 ? e === eo : !1;
	}
	function Tu() {
		var e = N.H;
		return N.H = Is, e === null ? Is : e;
	}
	function Eu() {
		var e = N.A;
		return N.A = Ll, e;
	}
	function Du() {
		Wl = 4, Bl || (Y & 4194048) !== Y && $a.current !== null || (Vl = !0), !(Gl & 134217727) && !(Kl & 134217727) || q === null || yu(q, Y, Jl, !1);
	}
	function Ou(e, t, n) {
		var r = K;
		K |= 2;
		var i = Tu(), a = Eu();
		(q !== e || Y !== t) && (nu = null, Su(e, t)), t = !1;
		var o = Wl;
		a: do
			try {
				if (X !== 0 && J !== null) {
					var s = J, c = zl;
					switch (X) {
						case 8:
							xu(), o = 6;
							break a;
						case 3:
						case 2:
						case 9:
						case 6:
							$a.current === null && (t = !0);
							var l = X;
							if (X = 0, zl = null, Pu(e, s, c, l), n && Vl) {
								o = 0;
								break a;
							}
							break;
						default: l = X, X = 0, zl = null, Pu(e, s, c, l);
					}
				}
				ku(), o = Wl;
				break;
			} catch (t) {
				Cu(e, t);
			}
		while (1);
		return t && e.shellSuspendCounter++, Ki = Gi = null, K = r, N.H = i, N.A = a, J === null && (q = null, Y = 0, ei()), o;
	}
	function ku() {
		for (; J !== null;) Mu(J);
	}
	function Au(e, t) {
		var n = K;
		K |= 2;
		var r = Tu(), i = Eu();
		q !== e || Y !== t ? (nu = null, tu = Ee() + 500, Su(e, t)) : Vl = qe(e, t);
		a: do
			try {
				if (X !== 0 && J !== null) {
					t = J;
					var o = zl;
					b: switch (X) {
						case 1:
							X = 0, zl = null, Pu(e, t, o, 1);
							break;
						case 2:
						case 9:
							if (Sa(o)) {
								X = 0, zl = null, Nu(t);
								break;
							}
							t = function() {
								X !== 2 && X !== 9 || q !== e || (X = 7), rd(e);
							}, o.then(t, t);
							break a;
						case 3:
							X = 7;
							break a;
						case 4:
							X = 5;
							break a;
						case 7:
							Sa(o) ? (X = 0, zl = null, Nu(t)) : (X = 0, zl = null, Pu(e, t, o, 7));
							break;
						case 5:
							var s = null;
							switch (J.tag) {
								case 26: s = J.memoizedState;
								case 5:
								case 27:
									var c = J;
									if (s ? Wf(s) : c.stateNode.complete) {
										X = 0, zl = null;
										var l = c.sibling;
										if (l !== null) J = l;
										else {
											var u = c.return;
											u === null ? J = null : (J = u, Fu(u));
										}
										break b;
									}
							}
							X = 0, zl = null, Pu(e, t, o, 5);
							break;
						case 6:
							X = 0, zl = null, Pu(e, t, o, 6);
							break;
						case 8:
							xu(), Wl = 6;
							break a;
						default: throw Error(a(462));
					}
				}
				ju();
				break;
			} catch (t) {
				Cu(e, t);
			}
		while (1);
		return Ki = Gi = null, N.H = r, N.A = i, K = n, J === null ? (q = null, Y = 0, ei(), Wl) : 0;
	}
	function ju() {
		for (; J !== null && !we();) Mu(J);
	}
	function Mu(e) {
		var t = Ac(e.alternate, e, Ul);
		e.memoizedProps = e.pendingProps, t === null ? Fu(e) : J = t;
	}
	function Nu(e) {
		var t = e, n = t.alternate;
		switch (t.tag) {
			case 15:
			case 0:
				t = mc(n, t, t.pendingProps, t.type, void 0, Y);
				break;
			case 11:
				t = mc(n, t, t.pendingProps, t.type.render, t.ref, Y);
				break;
			case 5: Eo(t);
			default: zc(n, t), t = J = di(t, Ul), t = Ac(n, t, Ul);
		}
		e.memoizedProps = e.pendingProps, t === null ? Fu(e) : J = t;
	}
	function Pu(e, t, n, r) {
		Ki = Gi = null, Eo(t), Oa = null, ka = 0;
		var i = t.return;
		try {
			if ($s(e, i, t, n, Y)) {
				Wl = 1, Js(e, vi(n, e.current)), J = null;
				return;
			}
		} catch (t) {
			if (i !== null) throw J = i, t;
			Wl = 1, Js(e, vi(n, e.current)), J = null;
			return;
		}
		t.flags & 32768 ? (B || r === 1 ? e = !0 : Vl || Y & 536870912 ? e = !1 : (Bl = e = !0, (r === 2 || r === 9 || r === 3 || r === 6) && (r = $a.current, r !== null && r.tag === 13 && (r.flags |= 16384))), Iu(t, e)) : Fu(t);
	}
	function Fu(e) {
		var t = e;
		do {
			if (t.flags & 32768) {
				Iu(t, Bl);
				return;
			}
			e = t.return;
			var n = Lc(t.alternate, t, Ul);
			if (n !== null) {
				J = n;
				return;
			}
			if (t = t.sibling, t !== null) {
				J = t;
				return;
			}
			J = t = e;
		} while (t !== null);
		Wl === 0 && (Wl = 5);
	}
	function Iu(e, t) {
		do {
			var n = Rc(e.alternate, e);
			if (n !== null) {
				n.flags &= 32767, J = n;
				return;
			}
			if (n = e.return, n !== null && (n.flags |= 32768, n.subtreeFlags = 0, n.deletions = null), !t && (e = e.sibling, e !== null)) {
				J = e;
				return;
			}
			J = e = n;
		} while (e !== null);
		Wl = 6, J = null;
	}
	function Lu(e, t, n, r, i, o, s, c, l) {
		e.cancelPendingCommit = null;
		do
			Hu();
		while (iu !== 0);
		if (K & 6) throw Error(a(327));
		if (t !== null) {
			if (t === e.current) throw Error(a(177));
			if (o = t.lanes | t.childLanes, o |= $r, Qe(e, n, o, s, c, l), e === q && (J = q = null, Y = 0), ou = t, au = e, su = n, cu = o, lu = i, uu = r, t.subtreeFlags & 10256 || t.flags & 10256 ? (e.callbackNode = null, e.callbackPriority = 0, Xu(Ae, function() {
				return Uu(), null;
			})) : (e.callbackNode = null, e.callbackPriority = 0), r = !!(t.flags & 13878), t.subtreeFlags & 13878 || r) {
				r = N.T, N.T = null, i = P.p, P.p = 2, s = K, K |= 4;
				try {
					il(e, t, n);
				} finally {
					K = s, P.p = i, N.T = r;
				}
			}
			iu = 1, Ru(), zu(), Bu();
		}
	}
	function Ru() {
		if (iu === 1) {
			iu = 0;
			var e = au, t = ou, n = !!(t.flags & 13878);
			if (t.subtreeFlags & 13878 || n) {
				n = N.T, N.T = null;
				var r = P.p;
				P.p = 2;
				var i = K;
				K |= 4;
				try {
					_l(t, e);
					var a = zd, o = Dr(e.containerInfo), s = a.focusedElem, c = a.selectionRange;
					if (o !== s && s && s.ownerDocument && Er(s.ownerDocument.documentElement, s)) {
						if (c !== null && Or(s)) {
							var l = c.start, u = c.end;
							if (u === void 0 && (u = l), "selectionStart" in s) s.selectionStart = l, s.selectionEnd = Math.min(u, s.value.length);
							else {
								var d = s.ownerDocument || document, f = d && d.defaultView || window;
								if (f.getSelection) {
									var p = f.getSelection(), m = s.textContent.length, h = Math.min(c.start, m), g = c.end === void 0 ? h : Math.min(c.end, m);
									!p.extend && h > g && (o = g, g = h, h = o);
									var _ = Tr(s, h), v = Tr(s, g);
									if (_ && v && (p.rangeCount !== 1 || p.anchorNode !== _.node || p.anchorOffset !== _.offset || p.focusNode !== v.node || p.focusOffset !== v.offset)) {
										var y = d.createRange();
										y.setStart(_.node, _.offset), p.removeAllRanges(), h > g ? (p.addRange(y), p.extend(v.node, v.offset)) : (y.setEnd(v.node, v.offset), p.addRange(y));
									}
								}
							}
						}
						for (d = [], p = s; p = p.parentNode;) p.nodeType === 1 && d.push({
							element: p,
							left: p.scrollLeft,
							top: p.scrollTop
						});
						for (typeof s.focus == "function" && s.focus(), s = 0; s < d.length; s++) {
							var b = d[s];
							b.element.scrollLeft = b.left, b.element.scrollTop = b.top;
						}
					}
					sp = !!Rd, zd = Rd = null;
				} finally {
					K = i, P.p = r, N.T = n;
				}
			}
			e.current = t, iu = 2;
		}
	}
	function zu() {
		if (iu === 2) {
			iu = 0;
			var e = au, t = ou, n = !!(t.flags & 8772);
			if (t.subtreeFlags & 8772 || n) {
				n = N.T, N.T = null;
				var r = P.p;
				P.p = 2;
				var i = K;
				K |= 4;
				try {
					al(e, t.alternate, t);
				} finally {
					K = i, P.p = r, N.T = n;
				}
			}
			iu = 3;
		}
	}
	function Bu() {
		if (iu === 4 || iu === 3) {
			iu = 0, Te();
			var e = au, t = ou, n = su, r = uu;
			t.subtreeFlags & 10256 || t.flags & 10256 ? iu = 5 : (iu = 0, ou = au = null, Vu(e, e.pendingLanes));
			var i = e.pendingLanes;
			if (i === 0 && (ru = null), rt(n), t = t.stateNode, Ie && typeof Ie.onCommitFiberRoot == "function") try {
				Ie.onCommitFiberRoot(Fe, t, void 0, (t.current.flags & 128) == 128);
			} catch {}
			if (r !== null) {
				t = N.T, i = P.p, P.p = 2, N.T = null;
				try {
					for (var a = e.onRecoverableError, o = 0; o < r.length; o++) {
						var s = r[o];
						a(s.value, { componentStack: s.stack });
					}
				} finally {
					N.T = t, P.p = i;
				}
			}
			su & 3 && Hu(), rd(e), i = e.pendingLanes, n & 261930 && i & 42 ? e === fu ? du++ : (du = 0, fu = e) : du = 0, id(0, !1);
		}
	}
	function Vu(e, t) {
		(e.pooledCacheLanes &= t) === 0 && (t = e.pooledCache, t != null && (e.pooledCache = null, ca(t)));
	}
	function Hu() {
		return Ru(), zu(), Bu(), Uu();
	}
	function Uu() {
		if (iu !== 5) return !1;
		var e = au, t = cu;
		cu = 0;
		var n = rt(su), r = N.T, i = P.p;
		try {
			P.p = 32 > n ? 32 : n, N.T = null, n = lu, lu = null;
			var o = au, s = su;
			if (iu = 0, ou = au = null, su = 0, K & 6) throw Error(a(331));
			var c = K;
			if (K |= 4, Pl(o.current), El(o, o.current, s, n), K = c, id(0, !1), Ie && typeof Ie.onPostCommitFiberRoot == "function") try {
				Ie.onPostCommitFiberRoot(Fe, o);
			} catch {}
			return !0;
		} finally {
			P.p = i, N.T = r, Vu(e, t);
		}
	}
	function Wu(e, t, n) {
		t = vi(n, t), t = Xs(e.stateNode, t, 2), e = Ba(e, t, 2), e !== null && (Ze(e, 2), rd(e));
	}
	function Z(e, t, n) {
		if (e.tag === 3) Wu(e, e, n);
		else for (; t !== null;) {
			if (t.tag === 3) {
				Wu(t, e, n);
				break;
			}
			if (t.tag === 1) {
				var r = t.stateNode;
				if (typeof t.type.getDerivedStateFromError == "function" || typeof r.componentDidCatch == "function" && (ru === null || !ru.has(r))) {
					e = vi(n, e), n = Zs(2), r = Ba(t, n, 2), r !== null && (Qs(n, r, t, e), Ze(r, 2), rd(r));
					break;
				}
			}
			t = t.return;
		}
	}
	function Gu(e, t, n) {
		var r = e.pingCache;
		if (r === null) {
			r = e.pingCache = new Rl();
			var i = /* @__PURE__ */ new Set();
			r.set(t, i);
		} else i = r.get(t), i === void 0 && (i = /* @__PURE__ */ new Set(), r.set(t, i));
		i.has(n) || (Hl = !0, i.add(n), e = Ku.bind(null, e, t, n), t.then(e, e));
	}
	function Ku(e, t, n) {
		var r = e.pingCache;
		r !== null && r.delete(t), e.pingedLanes |= e.suspendedLanes & n, e.warmLanes &= ~n, q === e && (Y & n) === n && (Wl === 4 || Wl === 3 && (Y & 62914560) === Y && 300 > Ee() - $l ? !(K & 2) && Su(e, 0) : ql |= n, Yl === Y && (Yl = 0)), rd(e);
	}
	function qu(e, t) {
		t === 0 && (t = Ye()), e = ri(e, t), e !== null && (Ze(e, t), rd(e));
	}
	function Ju(e) {
		var t = e.memoizedState, n = 0;
		t !== null && (n = t.retryLane), qu(e, n);
	}
	function Yu(e, t) {
		var n = 0;
		switch (e.tag) {
			case 31:
			case 13:
				var r = e.stateNode, i = e.memoizedState;
				i !== null && (n = i.retryLane);
				break;
			case 19:
				r = e.stateNode;
				break;
			case 22:
				r = e.stateNode._retryCache;
				break;
			default: throw Error(a(314));
		}
		r !== null && r.delete(t), qu(e, n);
	}
	function Xu(e, t) {
		return Se(e, t);
	}
	var Zu = null, Qu = null, $u = !1, ed = !1, td = !1, nd = 0;
	function rd(e) {
		e !== Qu && e.next === null && (Qu === null ? Zu = Qu = e : Qu = Qu.next = e), ed = !0, $u || ($u = !0, ud());
	}
	function id(e, t) {
		if (!td && ed) {
			td = !0;
			do
				for (var n = !1, r = Zu; r !== null;) {
					if (!t) {
						if (e !== 0) {
							var i = r.pendingLanes;
							if (i === 0) var a = 0;
							else {
								var o = r.suspendedLanes, s = r.pingedLanes;
								a = (1 << 31 - Re(42 | e) + 1) - 1, a &= i & ~(o & ~s), a = a & 201326741 ? a & 201326741 | 1 : a ? a | 2 : 0;
							}
							a !== 0 && (n = !0, ld(r, a));
						} else a = Y, a = Ke(r, r === q ? a : 0, r.cancelPendingCommit !== null || r.timeoutHandle !== -1), !(a & 3) || qe(r, a) || (n = !0, ld(r, a));
					}
					r = r.next;
				}
			while (n);
			td = !1;
		}
	}
	function ad() {
		od();
	}
	function od() {
		ed = $u = !1;
		var e = 0;
		nd !== 0 && Gd() && (e = nd);
		for (var t = Ee(), n = null, r = Zu; r !== null;) {
			var i = r.next, a = sd(r, t);
			a === 0 ? (r.next = null, n === null ? Zu = i : n.next = i, i === null && (Qu = n)) : (n = r, (e !== 0 || a & 3) && (ed = !0)), r = i;
		}
		iu !== 0 && iu !== 5 || id(e, !1), nd !== 0 && (nd = 0);
	}
	function sd(e, t) {
		for (var n = e.suspendedLanes, r = e.pingedLanes, i = e.expirationTimes, a = e.pendingLanes & -62914561; 0 < a;) {
			var o = 31 - Re(a), s = 1 << o, c = i[o];
			c === -1 ? ((s & n) === 0 || (s & r) !== 0) && (i[o] = Je(s, t)) : c <= t && (e.expiredLanes |= s), a &= ~s;
		}
		if (t = q, n = Y, n = Ke(e, e === t ? n : 0, e.cancelPendingCommit !== null || e.timeoutHandle !== -1), r = e.callbackNode, n === 0 || e === t && (X === 2 || X === 9) || e.cancelPendingCommit !== null) return r !== null && r !== null && Ce(r), e.callbackNode = null, e.callbackPriority = 0;
		if (!(n & 3) || qe(e, n)) {
			if (t = n & -n, t === e.callbackPriority) return t;
			switch (r !== null && Ce(r), rt(n)) {
				case 2:
				case 8:
					n = ke;
					break;
				case 32:
					n = Ae;
					break;
				case 268435456:
					n = Me;
					break;
				default: n = Ae;
			}
			return r = cd.bind(null, e), n = Se(n, r), e.callbackPriority = t, e.callbackNode = n, t;
		}
		return r !== null && r !== null && Ce(r), e.callbackPriority = 2, e.callbackNode = null, 2;
	}
	function cd(e, t) {
		if (iu !== 0 && iu !== 5) return e.callbackNode = null, e.callbackPriority = 0, null;
		var n = e.callbackNode;
		if (Hu() && e.callbackNode !== n) return null;
		var r = Y;
		return r = Ke(e, e === q ? r : 0, e.cancelPendingCommit !== null || e.timeoutHandle !== -1), r === 0 ? null : (gu(e, r, t), sd(e, Ee()), e.callbackNode != null && e.callbackNode === n ? cd.bind(null, e) : null);
	}
	function ld(e, t) {
		if (Hu()) return null;
		gu(e, t, !0);
	}
	function ud() {
		Yd(function() {
			K & 6 ? Se(Oe, ad) : od();
		});
	}
	function dd() {
		if (nd === 0) {
			var e = da;
			e === 0 && (e = He, He <<= 1, !(He & 261888) && (He = 256)), nd = e;
		}
		return nd;
	}
	function fd(e) {
		return e == null || typeof e == "symbol" || typeof e == "boolean" ? null : typeof e == "function" ? e : $t("" + e);
	}
	function pd(e, t) {
		var n = t.ownerDocument.createElement("input");
		return n.name = t.name, n.value = t.value, e.id && n.setAttribute("form", e.id), t.parentNode.insertBefore(n, t), e = new FormData(e), n.parentNode.removeChild(n), e;
	}
	function md(e, t, n, r, i) {
		if (t === "submit" && n && n.stateNode === i) {
			var a = fd((i[ct] || null).action), o = r.submitter;
			o && (t = (t = o[ct] || null) ? fd(t.formAction) : o.getAttribute("formAction"), t !== null && (a = t, o = null));
			var s = new xn("action", "action", null, r, i);
			e.push({
				event: s,
				listeners: [{
					instance: null,
					listener: function() {
						if (r.defaultPrevented) {
							if (nd !== 0) {
								var e = o ? pd(i, o) : new FormData(i);
								Ss(n, {
									pending: !0,
									data: e,
									method: i.method,
									action: a
								}, null, e);
							}
						} else typeof a == "function" && (s.preventDefault(), e = o ? pd(i, o) : new FormData(i), Ss(n, {
							pending: !0,
							data: e,
							method: i.method,
							action: a
						}, a, e));
					},
					currentTarget: i
				}]
			});
		}
	}
	for (var hd = 0; hd < Jr.length; hd++) {
		var gd = Jr[hd];
		Yr(gd.toLowerCase(), "on" + (gd[0].toUpperCase() + gd.slice(1)));
	}
	Yr(Br, "onAnimationEnd"), Yr(Vr, "onAnimationIteration"), Yr(Hr, "onAnimationStart"), Yr("dblclick", "onDoubleClick"), Yr("focusin", "onFocus"), Yr("focusout", "onBlur"), Yr(Ur, "onTransitionRun"), Yr(Wr, "onTransitionStart"), Yr(Gr, "onTransitionCancel"), Yr(Kr, "onTransitionEnd"), wt("onMouseEnter", ["mouseout", "mouseover"]), wt("onMouseLeave", ["mouseout", "mouseover"]), wt("onPointerEnter", ["pointerout", "pointerover"]), wt("onPointerLeave", ["pointerout", "pointerover"]), Ct("onChange", "change click focusin focusout input keydown keyup selectionchange".split(" ")), Ct("onSelect", "focusout contextmenu dragend focusin keydown keyup mousedown mouseup selectionchange".split(" ")), Ct("onBeforeInput", [
		"compositionend",
		"keypress",
		"textInput",
		"paste"
	]), Ct("onCompositionEnd", "compositionend focusout keydown keypress keyup mousedown".split(" ")), Ct("onCompositionStart", "compositionstart focusout keydown keypress keyup mousedown".split(" ")), Ct("onCompositionUpdate", "compositionupdate focusout keydown keypress keyup mousedown".split(" "));
	var _d = "abort canplay canplaythrough durationchange emptied encrypted ended error loadeddata loadedmetadata loadstart pause play playing progress ratechange resize seeked seeking stalled suspend timeupdate volumechange waiting".split(" "), vd = new Set("beforetoggle cancel close invalid load scroll scrollend toggle".split(" ").concat(_d));
	function yd(e, t) {
		t = !!(t & 4);
		for (var n = 0; n < e.length; n++) {
			var r = e[n], i = r.event;
			r = r.listeners;
			a: {
				var a = void 0;
				if (t) for (var o = r.length - 1; 0 <= o; o--) {
					var s = r[o], c = s.instance, l = s.currentTarget;
					if (s = s.listener, c !== a && i.isPropagationStopped()) break a;
					a = s, i.currentTarget = l;
					try {
						a(i);
					} catch (e) {
						Xr(e);
					}
					i.currentTarget = null, a = c;
				}
				else for (o = 0; o < r.length; o++) {
					if (s = r[o], c = s.instance, l = s.currentTarget, s = s.listener, c !== a && i.isPropagationStopped()) break a;
					a = s, i.currentTarget = l;
					try {
						a(i);
					} catch (e) {
						Xr(e);
					}
					i.currentTarget = null, a = c;
				}
			}
		}
	}
	function Q(e, t) {
		var n = t[ut];
		n === void 0 && (n = t[ut] = /* @__PURE__ */ new Set());
		var r = e + "__bubble";
		n.has(r) || (Cd(t, e, 2, !1), n.add(r));
	}
	function bd(e, t, n) {
		var r = 0;
		t && (r |= 4), Cd(n, e, r, t);
	}
	var xd = "_reactListening" + Math.random().toString(36).slice(2);
	function Sd(e) {
		if (!e[xd]) {
			e[xd] = !0, xt.forEach(function(t) {
				t !== "selectionchange" && (vd.has(t) || bd(t, !1, e), bd(t, !0, e));
			});
			var t = e.nodeType === 9 ? e : e.ownerDocument;
			t === null || t[xd] || (t[xd] = !0, bd("selectionchange", !1, t));
		}
	}
	function Cd(e, t, n, r) {
		switch (mp(t)) {
			case 2:
				var i = cp;
				break;
			case 8:
				i = lp;
				break;
			default: i = up;
		}
		n = i.bind(null, t, n, e), i = void 0, !un || t !== "touchstart" && t !== "touchmove" && t !== "wheel" || (i = !0), r ? i === void 0 ? e.addEventListener(t, n, !0) : e.addEventListener(t, n, {
			capture: !0,
			passive: i
		}) : i === void 0 ? e.addEventListener(t, n, !1) : e.addEventListener(t, n, { passive: i });
	}
	function wd(e, t, n, r, i) {
		var a = r;
		if (!(t & 1) && !(t & 2) && r !== null) a: for (;;) {
			if (r === null) return;
			var o = r.tag;
			if (o === 3 || o === 4) {
				var s = r.stateNode.containerInfo;
				if (s === i) break;
				if (o === 4) for (o = r.return; o !== null;) {
					var c = o.tag;
					if ((c === 3 || c === 4) && o.stateNode.containerInfo === i) return;
					o = o.return;
				}
				for (; s !== null;) {
					if (o = gt(s), o === null) return;
					if (c = o.tag, c === 5 || c === 6 || c === 26 || c === 27) {
						r = a = o;
						continue a;
					}
					s = s.parentNode;
				}
			}
			r = r.return;
		}
		sn(function() {
			var r = a, i = nn(n), o = [];
			a: {
				var s = qr.get(e);
				if (s !== void 0) {
					var c = xn, u = e;
					switch (e) {
						case "keypress": if (gn(n) === 0) break a;
						case "keydown":
						case "keyup":
							c = zn;
							break;
						case "focusin":
							u = "focus", c = An;
							break;
						case "focusout":
							u = "blur", c = An;
							break;
						case "beforeblur":
						case "afterblur":
							c = An;
							break;
						case "click": if (n.button === 2) break a;
						case "auxclick":
						case "dblclick":
						case "mousedown":
						case "mousemove":
						case "mouseup":
						case "mouseout":
						case "mouseover":
						case "contextmenu":
							c = On;
							break;
						case "drag":
						case "dragend":
						case "dragenter":
						case "dragexit":
						case "dragleave":
						case "dragover":
						case "dragstart":
						case "drop":
							c = kn;
							break;
						case "touchcancel":
						case "touchend":
						case "touchmove":
						case "touchstart":
							c = Vn;
							break;
						case Br:
						case Vr:
						case Hr:
							c = jn;
							break;
						case Kr:
							c = Hn;
							break;
						case "scroll":
						case "scrollend":
							c = Cn;
							break;
						case "wheel":
							c = Un;
							break;
						case "copy":
						case "cut":
						case "paste":
							c = Mn;
							break;
						case "gotpointercapture":
						case "lostpointercapture":
						case "pointercancel":
						case "pointerdown":
						case "pointermove":
						case "pointerout":
						case "pointerover":
						case "pointerup":
							c = Bn;
							break;
						case "toggle":
						case "beforetoggle": c = Wn;
					}
					var d = !!(t & 4), f = !d && (e === "scroll" || e === "scrollend"), p = d ? s === null ? null : s + "Capture" : s;
					d = [];
					for (var m = r, h; m !== null;) {
						var g = m;
						if (h = g.stateNode, g = g.tag, g !== 5 && g !== 26 && g !== 27 || h === null || p === null || (g = cn(m, p), g != null && d.push(Td(m, g, h))), f) break;
						m = m.return;
					}
					0 < d.length && (s = new c(s, u, null, n, i), o.push({
						event: s,
						listeners: d
					}));
				}
			}
			if (!(t & 7)) {
				a: {
					if (s = e === "mouseover" || e === "pointerover", c = e === "mouseout" || e === "pointerout", s && n !== tn && (u = n.relatedTarget || n.fromElement) && (gt(u) || u[lt])) break a;
					if ((c || s) && (s = i.window === i ? i : (s = i.ownerDocument) ? s.defaultView || s.parentWindow : window, c ? (u = n.relatedTarget || n.toElement, c = r, u = u ? gt(u) : null, u !== null && (f = l(u), d = u.tag, u !== f || d !== 5 && d !== 27 && d !== 6) && (u = null)) : (c = null, u = r), c !== u)) {
						if (d = On, g = "onMouseLeave", p = "onMouseEnter", m = "mouse", (e === "pointerout" || e === "pointerover") && (d = Bn, g = "onPointerLeave", p = "onPointerEnter", m = "pointer"), f = c == null ? s : vt(c), h = u == null ? s : vt(u), s = new d(g, m + "leave", c, n, i), s.target = f, s.relatedTarget = h, g = null, gt(i) === r && (d = new d(p, m + "enter", u, n, i), d.target = h, d.relatedTarget = f, g = d), f = g, c && u) b: {
							for (d = Dd, p = c, m = u, h = 0, g = p; g; g = d(g)) h++;
							g = 0;
							for (var _ = m; _; _ = d(_)) g++;
							for (; 0 < h - g;) p = d(p), h--;
							for (; 0 < g - h;) m = d(m), g--;
							for (; h--;) {
								if (p === m || m !== null && p === m.alternate) {
									d = p;
									break b;
								}
								p = d(p), m = d(m);
							}
							d = null;
						}
						else d = null;
						c !== null && Od(o, s, c, d, !1), u !== null && f !== null && Od(o, f, u, d, !0);
					}
				}
				a: {
					if (s = r ? vt(r) : window, c = s.nodeName && s.nodeName.toLowerCase(), c === "select" || c === "input" && s.type === "file") var v = ur;
					else if (ir(s)) {
						if (dr) v = br;
						else {
							v = vr;
							var y = _r;
						}
					} else c = s.nodeName, !c || c.toLowerCase() !== "input" || s.type !== "checkbox" && s.type !== "radio" ? r && Xt(r.elementType) && (v = ur) : v = yr;
					if (v &&= v(e, r)) {
						ar(o, v, n, i);
						break a;
					}
					y && y(e, s, r), e === "focusout" && r && s.type === "number" && r.memoizedProps.value != null && Ht(s, "number", s.value);
				}
				switch (y = r ? vt(r) : window, e) {
					case "focusin":
						(ir(y) || y.contentEditable === "true") && (Ar = y, jr = r, Mr = null);
						break;
					case "focusout":
						Mr = jr = Ar = null;
						break;
					case "mousedown":
						Nr = !0;
						break;
					case "contextmenu":
					case "mouseup":
					case "dragend":
						Nr = !1, Pr(o, n, i);
						break;
					case "selectionchange": if (kr) break;
					case "keydown":
					case "keyup": Pr(o, n, i);
				}
				var b;
				if (Kn) b: {
					switch (e) {
						case "compositionstart":
							var x = "onCompositionStart";
							break b;
						case "compositionend":
							x = "onCompositionEnd";
							break b;
						case "compositionupdate":
							x = "onCompositionUpdate";
							break b;
					}
					x = void 0;
				}
				else er ? Qn(e, n) && (x = "onCompositionEnd") : e === "keydown" && n.keyCode === 229 && (x = "onCompositionStart");
				x && (Yn && n.locale !== "ko" && (er || x !== "onCompositionStart" ? x === "onCompositionEnd" && er && (b = hn()) : (fn = i, pn = "value" in fn ? fn.value : fn.textContent, er = !0)), y = Ed(r, x), 0 < y.length && (x = new Nn(x, e, null, n, i), o.push({
					event: x,
					listeners: y
				}), b ? x.data = b : (b = $n(n), b !== null && (x.data = b)))), (b = Jn ? tr(e, n) : nr(e, n)) && (x = Ed(r, "onBeforeInput"), 0 < x.length && (y = new Nn("onBeforeInput", "beforeinput", null, n, i), o.push({
					event: y,
					listeners: x
				}), y.data = b)), md(o, e, r, n, i);
			}
			yd(o, t);
		});
	}
	function Td(e, t, n) {
		return {
			instance: e,
			listener: t,
			currentTarget: n
		};
	}
	function Ed(e, t) {
		for (var n = t + "Capture", r = []; e !== null;) {
			var i = e, a = i.stateNode;
			if (i = i.tag, i !== 5 && i !== 26 && i !== 27 || a === null || (i = cn(e, n), i != null && r.unshift(Td(e, i, a)), i = cn(e, t), i != null && r.push(Td(e, i, a))), e.tag === 3) return r;
			e = e.return;
		}
		return [];
	}
	function Dd(e) {
		if (e === null) return null;
		do
			e = e.return;
		while (e && e.tag !== 5 && e.tag !== 27);
		return e || null;
	}
	function Od(e, t, n, r, i) {
		for (var a = t._reactName, o = []; n !== null && n !== r;) {
			var s = n, c = s.alternate, l = s.stateNode;
			if (s = s.tag, c !== null && c === r) break;
			s !== 5 && s !== 26 && s !== 27 || l === null || (c = l, i ? (l = cn(n, a), l != null && o.unshift(Td(n, l, c))) : i || (l = cn(n, a), l != null && o.push(Td(n, l, c)))), n = n.return;
		}
		o.length !== 0 && e.push({
			event: t,
			listeners: o
		});
	}
	var kd = /\r\n?/g, Ad = /\u0000|\uFFFD/g;
	function jd(e) {
		return (typeof e == "string" ? e : "" + e).replace(kd, "\n").replace(Ad, "");
	}
	function Md(e, t) {
		return t = jd(t), jd(e) === t;
	}
	function $(e, t, n, r, i, o) {
		switch (n) {
			case "children":
				typeof r == "string" ? t === "body" || t === "textarea" && r === "" || Kt(e, r) : (typeof r == "number" || typeof r == "bigint") && t !== "body" && Kt(e, "" + r);
				break;
			case "className":
				At(e, "class", r);
				break;
			case "tabIndex":
				At(e, "tabindex", r);
				break;
			case "dir":
			case "role":
			case "viewBox":
			case "width":
			case "height":
				At(e, n, r);
				break;
			case "style":
				Yt(e, r, o);
				break;
			case "data": if (t !== "object") {
				At(e, "data", r);
				break;
			}
			case "src":
			case "href":
				if (r === "" && (t !== "a" || n !== "href")) {
					e.removeAttribute(n);
					break;
				}
				if (r == null || typeof r == "function" || typeof r == "symbol" || typeof r == "boolean") {
					e.removeAttribute(n);
					break;
				}
				r = $t("" + r), e.setAttribute(n, r);
				break;
			case "action":
			case "formAction":
				if (typeof r == "function") {
					e.setAttribute(n, "javascript:throw new Error('A React form was unexpectedly submitted. If you called form.submit() manually, consider using form.requestSubmit() instead. If you\\'re trying to use event.stopPropagation() in a submit event handler, consider also calling event.preventDefault().')");
					break;
				}
				if (typeof o == "function" && (n === "formAction" ? (t !== "input" && $(e, t, "name", i.name, i, null), $(e, t, "formEncType", i.formEncType, i, null), $(e, t, "formMethod", i.formMethod, i, null), $(e, t, "formTarget", i.formTarget, i, null)) : ($(e, t, "encType", i.encType, i, null), $(e, t, "method", i.method, i, null), $(e, t, "target", i.target, i, null))), r == null || typeof r == "symbol" || typeof r == "boolean") {
					e.removeAttribute(n);
					break;
				}
				r = $t("" + r), e.setAttribute(n, r);
				break;
			case "onClick":
				r != null && (e.onclick = en);
				break;
			case "onScroll":
				r != null && Q("scroll", e);
				break;
			case "onScrollEnd":
				r != null && Q("scrollend", e);
				break;
			case "dangerouslySetInnerHTML":
				if (r != null) {
					if (typeof r != "object" || !("__html" in r)) throw Error(a(61));
					if (n = r.__html, n != null) {
						if (i.children != null) throw Error(a(60));
						e.innerHTML = n;
					}
				}
				break;
			case "multiple":
				e.multiple = r && typeof r != "function" && typeof r != "symbol";
				break;
			case "muted":
				e.muted = r && typeof r != "function" && typeof r != "symbol";
				break;
			case "suppressContentEditableWarning":
			case "suppressHydrationWarning":
			case "defaultValue":
			case "defaultChecked":
			case "innerHTML":
			case "ref": break;
			case "autoFocus": break;
			case "xlinkHref":
				if (r == null || typeof r == "function" || typeof r == "boolean" || typeof r == "symbol") {
					e.removeAttribute("xlink:href");
					break;
				}
				n = $t("" + r), e.setAttributeNS("http://www.w3.org/1999/xlink", "xlink:href", n);
				break;
			case "contentEditable":
			case "spellCheck":
			case "draggable":
			case "value":
			case "autoReverse":
			case "externalResourcesRequired":
			case "focusable":
			case "preserveAlpha":
				r != null && typeof r != "function" && typeof r != "symbol" ? e.setAttribute(n, "" + r) : e.removeAttribute(n);
				break;
			case "inert":
			case "allowFullScreen":
			case "async":
			case "autoPlay":
			case "controls":
			case "default":
			case "defer":
			case "disabled":
			case "disablePictureInPicture":
			case "disableRemotePlayback":
			case "formNoValidate":
			case "hidden":
			case "loop":
			case "noModule":
			case "noValidate":
			case "open":
			case "playsInline":
			case "readOnly":
			case "required":
			case "reversed":
			case "scoped":
			case "seamless":
			case "itemScope":
				r && typeof r != "function" && typeof r != "symbol" ? e.setAttribute(n, "") : e.removeAttribute(n);
				break;
			case "capture":
			case "download":
				!0 === r ? e.setAttribute(n, "") : !1 !== r && r != null && typeof r != "function" && typeof r != "symbol" ? e.setAttribute(n, r) : e.removeAttribute(n);
				break;
			case "cols":
			case "rows":
			case "size":
			case "span":
				r != null && typeof r != "function" && typeof r != "symbol" && !isNaN(r) && 1 <= r ? e.setAttribute(n, r) : e.removeAttribute(n);
				break;
			case "rowSpan":
			case "start":
				r == null || typeof r == "function" || typeof r == "symbol" || isNaN(r) ? e.removeAttribute(n) : e.setAttribute(n, r);
				break;
			case "popover":
				Q("beforetoggle", e), Q("toggle", e), kt(e, "popover", r);
				break;
			case "xlinkActuate":
				jt(e, "http://www.w3.org/1999/xlink", "xlink:actuate", r);
				break;
			case "xlinkArcrole":
				jt(e, "http://www.w3.org/1999/xlink", "xlink:arcrole", r);
				break;
			case "xlinkRole":
				jt(e, "http://www.w3.org/1999/xlink", "xlink:role", r);
				break;
			case "xlinkShow":
				jt(e, "http://www.w3.org/1999/xlink", "xlink:show", r);
				break;
			case "xlinkTitle":
				jt(e, "http://www.w3.org/1999/xlink", "xlink:title", r);
				break;
			case "xlinkType":
				jt(e, "http://www.w3.org/1999/xlink", "xlink:type", r);
				break;
			case "xmlBase":
				jt(e, "http://www.w3.org/XML/1998/namespace", "xml:base", r);
				break;
			case "xmlLang":
				jt(e, "http://www.w3.org/XML/1998/namespace", "xml:lang", r);
				break;
			case "xmlSpace":
				jt(e, "http://www.w3.org/XML/1998/namespace", "xml:space", r);
				break;
			case "is":
				kt(e, "is", r);
				break;
			case "innerText":
			case "textContent": break;
			default: (!(2 < n.length) || n[0] !== "o" && n[0] !== "O" || n[1] !== "n" && n[1] !== "N") && (n = Zt.get(n) || n, kt(e, n, r));
		}
	}
	function Nd(e, t, n, r, i, o) {
		switch (n) {
			case "style":
				Yt(e, r, o);
				break;
			case "dangerouslySetInnerHTML":
				if (r != null) {
					if (typeof r != "object" || !("__html" in r)) throw Error(a(61));
					if (n = r.__html, n != null) {
						if (i.children != null) throw Error(a(60));
						e.innerHTML = n;
					}
				}
				break;
			case "children":
				typeof r == "string" ? Kt(e, r) : (typeof r == "number" || typeof r == "bigint") && Kt(e, "" + r);
				break;
			case "onScroll":
				r != null && Q("scroll", e);
				break;
			case "onScrollEnd":
				r != null && Q("scrollend", e);
				break;
			case "onClick":
				r != null && (e.onclick = en);
				break;
			case "suppressContentEditableWarning":
			case "suppressHydrationWarning":
			case "innerHTML":
			case "ref": break;
			case "innerText":
			case "textContent": break;
			default: if (!St.hasOwnProperty(n)) a: {
				if (n[0] === "o" && n[1] === "n" && (i = n.endsWith("Capture"), t = n.slice(2, i ? n.length - 7 : void 0), o = e[ct] || null, o = o == null ? null : o[n], typeof o == "function" && e.removeEventListener(t, o, i), typeof r == "function")) {
					typeof o != "function" && o !== null && (n in e ? e[n] = null : e.hasAttribute(n) && e.removeAttribute(n)), e.addEventListener(t, r, i);
					break a;
				}
				n in e ? e[n] = r : !0 === r ? e.setAttribute(n, "") : kt(e, n, r);
			}
		}
	}
	function Pd(e, t, n) {
		switch (t) {
			case "div":
			case "span":
			case "svg":
			case "path":
			case "a":
			case "g":
			case "p":
			case "li": break;
			case "img":
				Q("error", e), Q("load", e);
				var r = !1, i = !1, o;
				for (o in n) if (n.hasOwnProperty(o)) {
					var s = n[o];
					if (s != null) switch (o) {
						case "src":
							r = !0;
							break;
						case "srcSet":
							i = !0;
							break;
						case "children":
						case "dangerouslySetInnerHTML": throw Error(a(137, t));
						default: $(e, t, o, s, n, null);
					}
				}
				i && $(e, t, "srcSet", n.srcSet, n, null), r && $(e, t, "src", n.src, n, null);
				return;
			case "input":
				Q("invalid", e);
				var c = o = s = i = null, l = null, u = null;
				for (r in n) if (n.hasOwnProperty(r)) {
					var d = n[r];
					if (d != null) switch (r) {
						case "name":
							i = d;
							break;
						case "type":
							s = d;
							break;
						case "checked":
							l = d;
							break;
						case "defaultChecked":
							u = d;
							break;
						case "value":
							o = d;
							break;
						case "defaultValue":
							c = d;
							break;
						case "children":
						case "dangerouslySetInnerHTML":
							if (d != null) throw Error(a(137, t));
							break;
						default: $(e, t, r, d, n, null);
					}
				}
				Vt(e, o, c, l, u, s, i, !1);
				return;
			case "select":
				for (i in Q("invalid", e), r = s = o = null, n) if (n.hasOwnProperty(i) && (c = n[i], c != null)) switch (i) {
					case "value":
						o = c;
						break;
					case "defaultValue":
						s = c;
						break;
					case "multiple": r = c;
					default: $(e, t, i, c, n, null);
				}
				t = o, n = s, e.multiple = !!r, t == null ? n != null && Ut(e, !!r, n, !0) : Ut(e, !!r, t, !1);
				return;
			case "textarea":
				for (s in Q("invalid", e), o = i = r = null, n) if (n.hasOwnProperty(s) && (c = n[s], c != null)) switch (s) {
					case "value":
						r = c;
						break;
					case "defaultValue":
						i = c;
						break;
					case "children":
						o = c;
						break;
					case "dangerouslySetInnerHTML":
						if (c != null) throw Error(a(91));
						break;
					default: $(e, t, s, c, n, null);
				}
				Gt(e, r, i, o);
				return;
			case "option":
				for (l in n) if (n.hasOwnProperty(l) && (r = n[l], r != null)) switch (l) {
					case "selected":
						e.selected = r && typeof r != "function" && typeof r != "symbol";
						break;
					default: $(e, t, l, r, n, null);
				}
				return;
			case "dialog":
				Q("beforetoggle", e), Q("toggle", e), Q("cancel", e), Q("close", e);
				break;
			case "iframe":
			case "object":
				Q("load", e);
				break;
			case "video":
			case "audio":
				for (r = 0; r < _d.length; r++) Q(_d[r], e);
				break;
			case "image":
				Q("error", e), Q("load", e);
				break;
			case "details":
				Q("toggle", e);
				break;
			case "embed":
			case "source":
			case "link": Q("error", e), Q("load", e);
			case "area":
			case "base":
			case "br":
			case "col":
			case "hr":
			case "keygen":
			case "meta":
			case "param":
			case "track":
			case "wbr":
			case "menuitem":
				for (u in n) if (n.hasOwnProperty(u) && (r = n[u], r != null)) switch (u) {
					case "children":
					case "dangerouslySetInnerHTML": throw Error(a(137, t));
					default: $(e, t, u, r, n, null);
				}
				return;
			default: if (Xt(t)) {
				for (d in n) n.hasOwnProperty(d) && (r = n[d], r !== void 0 && Nd(e, t, d, r, n, void 0));
				return;
			}
		}
		for (c in n) n.hasOwnProperty(c) && (r = n[c], r != null && $(e, t, c, r, n, null));
	}
	function Fd(e, t, n, r) {
		switch (t) {
			case "div":
			case "span":
			case "svg":
			case "path":
			case "a":
			case "g":
			case "p":
			case "li": break;
			case "input":
				var i = null, o = null, s = null, c = null, l = null, u = null, d = null;
				for (m in n) {
					var f = n[m];
					if (n.hasOwnProperty(m) && f != null) switch (m) {
						case "checked": break;
						case "value": break;
						case "defaultValue": l = f;
						default: r.hasOwnProperty(m) || $(e, t, m, null, r, f);
					}
				}
				for (var p in r) {
					var m = r[p];
					if (f = n[p], r.hasOwnProperty(p) && (m != null || f != null)) switch (p) {
						case "type":
							o = m;
							break;
						case "name":
							i = m;
							break;
						case "checked":
							u = m;
							break;
						case "defaultChecked":
							d = m;
							break;
						case "value":
							s = m;
							break;
						case "defaultValue":
							c = m;
							break;
						case "children":
						case "dangerouslySetInnerHTML":
							if (m != null) throw Error(a(137, t));
							break;
						default: m !== f && $(e, t, p, m, r, f);
					}
				}
				Bt(e, s, c, l, u, d, o, i);
				return;
			case "select":
				for (o in m = s = c = p = null, n) if (l = n[o], n.hasOwnProperty(o) && l != null) switch (o) {
					case "value": break;
					case "multiple": m = l;
					default: r.hasOwnProperty(o) || $(e, t, o, null, r, l);
				}
				for (i in r) if (o = r[i], l = n[i], r.hasOwnProperty(i) && (o != null || l != null)) switch (i) {
					case "value":
						p = o;
						break;
					case "defaultValue":
						c = o;
						break;
					case "multiple": s = o;
					default: o !== l && $(e, t, i, o, r, l);
				}
				t = c, n = s, r = m, p == null ? !!r != !!n && (t == null ? Ut(e, !!n, n ? [] : "", !1) : Ut(e, !!n, t, !0)) : Ut(e, !!n, p, !1);
				return;
			case "textarea":
				for (c in m = p = null, n) if (i = n[c], n.hasOwnProperty(c) && i != null && !r.hasOwnProperty(c)) switch (c) {
					case "value": break;
					case "children": break;
					default: $(e, t, c, null, r, i);
				}
				for (s in r) if (i = r[s], o = n[s], r.hasOwnProperty(s) && (i != null || o != null)) switch (s) {
					case "value":
						p = i;
						break;
					case "defaultValue":
						m = i;
						break;
					case "children": break;
					case "dangerouslySetInnerHTML":
						if (i != null) throw Error(a(91));
						break;
					default: i !== o && $(e, t, s, i, r, o);
				}
				Wt(e, p, m);
				return;
			case "option":
				for (var h in n) if (p = n[h], n.hasOwnProperty(h) && p != null && !r.hasOwnProperty(h)) switch (h) {
					case "selected":
						e.selected = !1;
						break;
					default: $(e, t, h, null, r, p);
				}
				for (l in r) if (p = r[l], m = n[l], r.hasOwnProperty(l) && p !== m && (p != null || m != null)) switch (l) {
					case "selected":
						e.selected = p && typeof p != "function" && typeof p != "symbol";
						break;
					default: $(e, t, l, p, r, m);
				}
				return;
			case "img":
			case "link":
			case "area":
			case "base":
			case "br":
			case "col":
			case "embed":
			case "hr":
			case "keygen":
			case "meta":
			case "param":
			case "source":
			case "track":
			case "wbr":
			case "menuitem":
				for (var g in n) p = n[g], n.hasOwnProperty(g) && p != null && !r.hasOwnProperty(g) && $(e, t, g, null, r, p);
				for (u in r) if (p = r[u], m = n[u], r.hasOwnProperty(u) && p !== m && (p != null || m != null)) switch (u) {
					case "children":
					case "dangerouslySetInnerHTML":
						if (p != null) throw Error(a(137, t));
						break;
					default: $(e, t, u, p, r, m);
				}
				return;
			default: if (Xt(t)) {
				for (var _ in n) p = n[_], n.hasOwnProperty(_) && p !== void 0 && !r.hasOwnProperty(_) && Nd(e, t, _, void 0, r, p);
				for (d in r) p = r[d], m = n[d], !r.hasOwnProperty(d) || p === m || p === void 0 && m === void 0 || Nd(e, t, d, p, r, m);
				return;
			}
		}
		for (var v in n) p = n[v], n.hasOwnProperty(v) && p != null && !r.hasOwnProperty(v) && $(e, t, v, null, r, p);
		for (f in r) p = r[f], m = n[f], !r.hasOwnProperty(f) || p === m || p == null && m == null || $(e, t, f, p, r, m);
	}
	function Id(e) {
		switch (e) {
			case "css":
			case "script":
			case "font":
			case "img":
			case "image":
			case "input":
			case "link": return !0;
			default: return !1;
		}
	}
	function Ld() {
		if (typeof performance.getEntriesByType == "function") {
			for (var e = 0, t = 0, n = performance.getEntriesByType("resource"), r = 0; r < n.length; r++) {
				var i = n[r], a = i.transferSize, o = i.initiatorType, s = i.duration;
				if (a && s && Id(o)) {
					for (o = 0, s = i.responseEnd, r += 1; r < n.length; r++) {
						var c = n[r], l = c.startTime;
						if (l > s) break;
						var u = c.transferSize, d = c.initiatorType;
						u && Id(d) && (c = c.responseEnd, o += u * (c < s ? 1 : (s - l) / (c - l)));
					}
					if (--r, t += 8 * (a + o) / (i.duration / 1e3), e++, 10 < e) break;
				}
			}
			if (0 < e) return t / e / 1e6;
		}
		return navigator.connection && (e = navigator.connection.downlink, typeof e == "number") ? e : 5;
	}
	var Rd = null, zd = null;
	function Bd(e) {
		return e.nodeType === 9 ? e : e.ownerDocument;
	}
	function Vd(e) {
		switch (e) {
			case "http://www.w3.org/2000/svg": return 1;
			case "http://www.w3.org/1998/Math/MathML": return 2;
			default: return 0;
		}
	}
	function Hd(e, t) {
		if (e === 0) switch (t) {
			case "svg": return 1;
			case "math": return 2;
			default: return 0;
		}
		return e === 1 && t === "foreignObject" ? 0 : e;
	}
	function Ud(e, t) {
		return e === "textarea" || e === "noscript" || typeof t.children == "string" || typeof t.children == "number" || typeof t.children == "bigint" || typeof t.dangerouslySetInnerHTML == "object" && t.dangerouslySetInnerHTML !== null && t.dangerouslySetInnerHTML.__html != null;
	}
	var Wd = null;
	function Gd() {
		var e = window.event;
		return e && e.type === "popstate" ? e !== Wd && (Wd = e, !0) : (Wd = null, !1);
	}
	var Kd = typeof setTimeout == "function" ? setTimeout : void 0, qd = typeof clearTimeout == "function" ? clearTimeout : void 0, Jd = typeof Promise == "function" ? Promise : void 0, Yd = typeof queueMicrotask == "function" ? queueMicrotask : Jd === void 0 ? Kd : function(e) {
		return Jd.resolve(null).then(e).catch(Xd);
	};
	function Xd(e) {
		setTimeout(function() {
			throw e;
		});
	}
	function Zd(e) {
		return e === "head";
	}
	function Qd(e, t) {
		var n = t, r = 0;
		do {
			var i = n.nextSibling;
			if (e.removeChild(n), i && i.nodeType === 8) {
				if (n = i.data, n === "/$" || n === "/&") {
					if (r === 0) {
						e.removeChild(i), Np(t);
						return;
					}
					r--;
				} else if (n === "$" || n === "$?" || n === "$~" || n === "$!" || n === "&") r++;
				else if (n === "html") pf(e.ownerDocument.documentElement);
				else if (n === "head") {
					n = e.ownerDocument.head, pf(n);
					for (var a = n.firstChild; a;) {
						var o = a.nextSibling, s = a.nodeName;
						a[mt] || s === "SCRIPT" || s === "STYLE" || s === "LINK" && a.rel.toLowerCase() === "stylesheet" || n.removeChild(a), a = o;
					}
				} else n === "body" && pf(e.ownerDocument.body);
			}
			n = i;
		} while (n);
		Np(t);
	}
	function $d(e, t) {
		var n = e;
		e = 0;
		do {
			var r = n.nextSibling;
			if (n.nodeType === 1 ? t ? (n._stashedDisplay = n.style.display, n.style.display = "none") : (n.style.display = n._stashedDisplay || "", n.getAttribute("style") === "" && n.removeAttribute("style")) : n.nodeType === 3 && (t ? (n._stashedText = n.nodeValue, n.nodeValue = "") : n.nodeValue = n._stashedText || ""), r && r.nodeType === 8) {
				if (n = r.data, n === "/$") {
					if (e === 0) break;
					e--;
				} else n !== "$" && n !== "$?" && n !== "$~" && n !== "$!" || e++;
			}
			n = r;
		} while (n);
	}
	function ef(e) {
		var t = e.firstChild;
		for (t && t.nodeType === 10 && (t = t.nextSibling); t;) {
			var n = t;
			switch (t = t.nextSibling, n.nodeName) {
				case "HTML":
				case "HEAD":
				case "BODY":
					ef(n), ht(n);
					continue;
				case "SCRIPT":
				case "STYLE": continue;
				case "LINK": if (n.rel.toLowerCase() === "stylesheet") continue;
			}
			e.removeChild(n);
		}
	}
	function tf(e, t, n, r) {
		for (; e.nodeType === 1;) {
			var i = n;
			if (e.nodeName.toLowerCase() !== t.toLowerCase()) {
				if (!r && (e.nodeName !== "INPUT" || e.type !== "hidden")) break;
			} else if (!r) {
				if (t === "input" && e.type === "hidden") {
					var a = i.name == null ? null : "" + i.name;
					if (i.type === "hidden" && e.getAttribute("name") === a) return e;
				} else return e;
			} else if (!e[mt]) switch (t) {
				case "meta":
					if (!e.hasAttribute("itemprop")) break;
					return e;
				case "link":
					if (a = e.getAttribute("rel"), a === "stylesheet" && e.hasAttribute("data-precedence") || a !== i.rel || e.getAttribute("href") !== (i.href == null || i.href === "" ? null : i.href) || e.getAttribute("crossorigin") !== (i.crossOrigin == null ? null : i.crossOrigin) || e.getAttribute("title") !== (i.title == null ? null : i.title)) break;
					return e;
				case "style":
					if (e.hasAttribute("data-precedence")) break;
					return e;
				case "script":
					if (a = e.getAttribute("src"), (a !== (i.src == null ? null : i.src) || e.getAttribute("type") !== (i.type == null ? null : i.type) || e.getAttribute("crossorigin") !== (i.crossOrigin == null ? null : i.crossOrigin)) && a && e.hasAttribute("async") && !e.hasAttribute("itemprop")) break;
					return e;
				default: return e;
			}
			if (e = cf(e.nextSibling), e === null) break;
		}
		return null;
	}
	function nf(e, t, n) {
		if (t === "") return null;
		for (; e.nodeType !== 3;) if ((e.nodeType !== 1 || e.nodeName !== "INPUT" || e.type !== "hidden") && !n || (e = cf(e.nextSibling), e === null)) return null;
		return e;
	}
	function rf(e, t) {
		for (; e.nodeType !== 8;) if ((e.nodeType !== 1 || e.nodeName !== "INPUT" || e.type !== "hidden") && !t || (e = cf(e.nextSibling), e === null)) return null;
		return e;
	}
	function af(e) {
		return e.data === "$?" || e.data === "$~";
	}
	function of(e) {
		return e.data === "$!" || e.data === "$?" && e.ownerDocument.readyState !== "loading";
	}
	function sf(e, t) {
		var n = e.ownerDocument;
		if (e.data === "$~") e._reactRetry = t;
		else if (e.data !== "$?" || n.readyState !== "loading") t();
		else {
			var r = function() {
				t(), n.removeEventListener("DOMContentLoaded", r);
			};
			n.addEventListener("DOMContentLoaded", r), e._reactRetry = r;
		}
	}
	function cf(e) {
		for (; e != null; e = e.nextSibling) {
			var t = e.nodeType;
			if (t === 1 || t === 3) break;
			if (t === 8) {
				if (t = e.data, t === "$" || t === "$!" || t === "$?" || t === "$~" || t === "&" || t === "F!" || t === "F") break;
				if (t === "/$" || t === "/&") return null;
			}
		}
		return e;
	}
	var lf = null;
	function uf(e) {
		e = e.nextSibling;
		for (var t = 0; e;) {
			if (e.nodeType === 8) {
				var n = e.data;
				if (n === "/$" || n === "/&") {
					if (t === 0) return cf(e.nextSibling);
					t--;
				} else n !== "$" && n !== "$!" && n !== "$?" && n !== "$~" && n !== "&" || t++;
			}
			e = e.nextSibling;
		}
		return null;
	}
	function df(e) {
		e = e.previousSibling;
		for (var t = 0; e;) {
			if (e.nodeType === 8) {
				var n = e.data;
				if (n === "$" || n === "$!" || n === "$?" || n === "$~" || n === "&") {
					if (t === 0) return e;
					t--;
				} else n !== "/$" && n !== "/&" || t++;
			}
			e = e.previousSibling;
		}
		return null;
	}
	function ff(e, t, n) {
		switch (t = Bd(n), e) {
			case "html":
				if (e = t.documentElement, !e) throw Error(a(452));
				return e;
			case "head":
				if (e = t.head, !e) throw Error(a(453));
				return e;
			case "body":
				if (e = t.body, !e) throw Error(a(454));
				return e;
			default: throw Error(a(451));
		}
	}
	function pf(e) {
		for (var t = e.attributes; t.length;) e.removeAttributeNode(t[0]);
		ht(e);
	}
	var mf = /* @__PURE__ */ new Map(), hf = /* @__PURE__ */ new Set();
	function gf(e) {
		return typeof e.getRootNode == "function" ? e.getRootNode() : e.nodeType === 9 ? e : e.ownerDocument;
	}
	var _f = P.d;
	P.d = {
		f: vf,
		r: yf,
		D: Sf,
		C: Cf,
		L: wf,
		m: Tf,
		X: Df,
		S: Ef,
		M: Of
	};
	function vf() {
		var e = _f.f(), t = bu();
		return e || t;
	}
	function yf(e) {
		var t = _t(e);
		t !== null && t.tag === 5 && t.type === "form" ? ws(t) : _f.r(e);
	}
	var bf = typeof document > "u" ? null : document;
	function xf(e, t, n) {
		var r = bf;
		if (r && typeof t == "string" && t) {
			var i = zt(t);
			i = "link[rel=\"" + e + "\"][href=\"" + i + "\"]", typeof n == "string" && (i += "[crossorigin=\"" + n + "\"]"), hf.has(i) || (hf.add(i), e = {
				rel: e,
				crossOrigin: n,
				href: t
			}, r.querySelector(i) === null && (t = r.createElement("link"), Pd(t, "link", e), bt(t), r.head.appendChild(t)));
		}
	}
	function Sf(e) {
		_f.D(e), xf("dns-prefetch", e, null);
	}
	function Cf(e, t) {
		_f.C(e, t), xf("preconnect", e, t);
	}
	function wf(e, t, n) {
		_f.L(e, t, n);
		var r = bf;
		if (r && e && t) {
			var i = "link[rel=\"preload\"][as=\"" + zt(t) + "\"]";
			t === "image" && n && n.imageSrcSet ? (i += "[imagesrcset=\"" + zt(n.imageSrcSet) + "\"]", typeof n.imageSizes == "string" && (i += "[imagesizes=\"" + zt(n.imageSizes) + "\"]")) : i += "[href=\"" + zt(e) + "\"]";
			var a = i;
			switch (t) {
				case "style":
					a = Af(e);
					break;
				case "script": a = Pf(e);
			}
			mf.has(a) || (e = h({
				rel: "preload",
				href: t === "image" && n && n.imageSrcSet ? void 0 : e,
				as: t
			}, n), mf.set(a, e), r.querySelector(i) !== null || t === "style" && r.querySelector(jf(a)) || t === "script" && r.querySelector(Ff(a)) || (t = r.createElement("link"), Pd(t, "link", e), bt(t), r.head.appendChild(t)));
		}
	}
	function Tf(e, t) {
		_f.m(e, t);
		var n = bf;
		if (n && e) {
			var r = t && typeof t.as == "string" ? t.as : "script", i = "link[rel=\"modulepreload\"][as=\"" + zt(r) + "\"][href=\"" + zt(e) + "\"]", a = i;
			switch (r) {
				case "audioworklet":
				case "paintworklet":
				case "serviceworker":
				case "sharedworker":
				case "worker":
				case "script": a = Pf(e);
			}
			if (!mf.has(a) && (e = h({
				rel: "modulepreload",
				href: e
			}, t), mf.set(a, e), n.querySelector(i) === null)) {
				switch (r) {
					case "audioworklet":
					case "paintworklet":
					case "serviceworker":
					case "sharedworker":
					case "worker":
					case "script": if (n.querySelector(Ff(a))) return;
				}
				r = n.createElement("link"), Pd(r, "link", e), bt(r), n.head.appendChild(r);
			}
		}
	}
	function Ef(e, t, n) {
		_f.S(e, t, n);
		var r = bf;
		if (r && e) {
			var i = yt(r).hoistableStyles, a = Af(e);
			t ||= "default";
			var o = i.get(a);
			if (!o) {
				var s = {
					loading: 0,
					preload: null
				};
				if (o = r.querySelector(jf(a))) s.loading = 5;
				else {
					e = h({
						rel: "stylesheet",
						href: e,
						"data-precedence": t
					}, n), (n = mf.get(a)) && Rf(e, n);
					var c = o = r.createElement("link");
					bt(c), Pd(c, "link", e), c._p = new Promise(function(e, t) {
						c.onload = e, c.onerror = t;
					}), c.addEventListener("load", function() {
						s.loading |= 1;
					}), c.addEventListener("error", function() {
						s.loading |= 2;
					}), s.loading |= 4, Lf(o, t, r);
				}
				o = {
					type: "stylesheet",
					instance: o,
					count: 1,
					state: s
				}, i.set(a, o);
			}
		}
	}
	function Df(e, t) {
		_f.X(e, t);
		var n = bf;
		if (n && e) {
			var r = yt(n).hoistableScripts, i = Pf(e), a = r.get(i);
			a || (a = n.querySelector(Ff(i)), a || (e = h({
				src: e,
				async: !0
			}, t), (t = mf.get(i)) && zf(e, t), a = n.createElement("script"), bt(a), Pd(a, "link", e), n.head.appendChild(a)), a = {
				type: "script",
				instance: a,
				count: 1,
				state: null
			}, r.set(i, a));
		}
	}
	function Of(e, t) {
		_f.M(e, t);
		var n = bf;
		if (n && e) {
			var r = yt(n).hoistableScripts, i = Pf(e), a = r.get(i);
			a || (a = n.querySelector(Ff(i)), a || (e = h({
				src: e,
				async: !0,
				type: "module"
			}, t), (t = mf.get(i)) && zf(e, t), a = n.createElement("script"), bt(a), Pd(a, "link", e), n.head.appendChild(a)), a = {
				type: "script",
				instance: a,
				count: 1,
				state: null
			}, r.set(i, a));
		}
	}
	function kf(e, t, n, r) {
		var i = (i = ce.current) ? gf(i) : null;
		if (!i) throw Error(a(446));
		switch (e) {
			case "meta":
			case "title": return null;
			case "style": return typeof n.precedence == "string" && typeof n.href == "string" ? (t = Af(n.href), n = yt(i).hoistableStyles, r = n.get(t), r || (r = {
				type: "style",
				instance: null,
				count: 0,
				state: null
			}, n.set(t, r)), r) : {
				type: "void",
				instance: null,
				count: 0,
				state: null
			};
			case "link":
				if (n.rel === "stylesheet" && typeof n.href == "string" && typeof n.precedence == "string") {
					e = Af(n.href);
					var o = yt(i).hoistableStyles, s = o.get(e);
					if (s || (i = i.ownerDocument || i, s = {
						type: "stylesheet",
						instance: null,
						count: 0,
						state: {
							loading: 0,
							preload: null
						}
					}, o.set(e, s), (o = i.querySelector(jf(e))) && !o._p && (s.instance = o, s.state.loading = 5), mf.has(e) || (n = {
						rel: "preload",
						as: "style",
						href: n.href,
						crossOrigin: n.crossOrigin,
						integrity: n.integrity,
						media: n.media,
						hrefLang: n.hrefLang,
						referrerPolicy: n.referrerPolicy
					}, mf.set(e, n), o || Nf(i, e, n, s.state))), t && r === null) throw Error(a(528, ""));
					return s;
				}
				if (t && r !== null) throw Error(a(529, ""));
				return null;
			case "script": return t = n.async, n = n.src, typeof n == "string" && t && typeof t != "function" && typeof t != "symbol" ? (t = Pf(n), n = yt(i).hoistableScripts, r = n.get(t), r || (r = {
				type: "script",
				instance: null,
				count: 0,
				state: null
			}, n.set(t, r)), r) : {
				type: "void",
				instance: null,
				count: 0,
				state: null
			};
			default: throw Error(a(444, e));
		}
	}
	function Af(e) {
		return "href=\"" + zt(e) + "\"";
	}
	function jf(e) {
		return "link[rel=\"stylesheet\"][" + e + "]";
	}
	function Mf(e) {
		return h({}, e, {
			"data-precedence": e.precedence,
			precedence: null
		});
	}
	function Nf(e, t, n, r) {
		e.querySelector("link[rel=\"preload\"][as=\"style\"][" + t + "]") ? r.loading = 1 : (t = e.createElement("link"), r.preload = t, t.addEventListener("load", function() {
			return r.loading |= 1;
		}), t.addEventListener("error", function() {
			return r.loading |= 2;
		}), Pd(t, "link", n), bt(t), e.head.appendChild(t));
	}
	function Pf(e) {
		return "[src=\"" + zt(e) + "\"]";
	}
	function Ff(e) {
		return "script[async]" + e;
	}
	function If(e, t, n) {
		if (t.count++, t.instance === null) switch (t.type) {
			case "style":
				var r = e.querySelector("style[data-href~=\"" + zt(n.href) + "\"]");
				if (r) return t.instance = r, bt(r), r;
				var i = h({}, n, {
					"data-href": n.href,
					"data-precedence": n.precedence,
					href: null,
					precedence: null
				});
				return r = (e.ownerDocument || e).createElement("style"), bt(r), Pd(r, "style", i), Lf(r, n.precedence, e), t.instance = r;
			case "stylesheet":
				i = Af(n.href);
				var o = e.querySelector(jf(i));
				if (o) return t.state.loading |= 4, t.instance = o, bt(o), o;
				r = Mf(n), (i = mf.get(i)) && Rf(r, i), o = (e.ownerDocument || e).createElement("link"), bt(o);
				var s = o;
				return s._p = new Promise(function(e, t) {
					s.onload = e, s.onerror = t;
				}), Pd(o, "link", r), t.state.loading |= 4, Lf(o, n.precedence, e), t.instance = o;
			case "script": return o = Pf(n.src), (i = e.querySelector(Ff(o))) ? (t.instance = i, bt(i), i) : (r = n, (i = mf.get(o)) && (r = h({}, n), zf(r, i)), e = e.ownerDocument || e, i = e.createElement("script"), bt(i), Pd(i, "link", r), e.head.appendChild(i), t.instance = i);
			case "void": return null;
			default: throw Error(a(443, t.type));
		}
		else t.type === "stylesheet" && !(t.state.loading & 4) && (r = t.instance, t.state.loading |= 4, Lf(r, n.precedence, e));
		return t.instance;
	}
	function Lf(e, t, n) {
		for (var r = n.querySelectorAll("link[rel=\"stylesheet\"][data-precedence],style[data-precedence]"), i = r.length ? r[r.length - 1] : null, a = i, o = 0; o < r.length; o++) {
			var s = r[o];
			if (s.dataset.precedence === t) a = s;
			else if (a !== i) break;
		}
		a ? a.parentNode.insertBefore(e, a.nextSibling) : (t = n.nodeType === 9 ? n.head : n, t.insertBefore(e, t.firstChild));
	}
	function Rf(e, t) {
		e.crossOrigin ??= t.crossOrigin, e.referrerPolicy ??= t.referrerPolicy, e.title ??= t.title;
	}
	function zf(e, t) {
		e.crossOrigin ??= t.crossOrigin, e.referrerPolicy ??= t.referrerPolicy, e.integrity ??= t.integrity;
	}
	var Bf = null;
	function Vf(e, t, n) {
		if (Bf === null) {
			var r = /* @__PURE__ */ new Map(), i = Bf = /* @__PURE__ */ new Map();
			i.set(n, r);
		} else i = Bf, r = i.get(n), r || (r = /* @__PURE__ */ new Map(), i.set(n, r));
		if (r.has(e)) return r;
		for (r.set(e, null), n = n.getElementsByTagName(e), i = 0; i < n.length; i++) {
			var a = n[i];
			if (!(a[mt] || a[st] || e === "link" && a.getAttribute("rel") === "stylesheet") && a.namespaceURI !== "http://www.w3.org/2000/svg") {
				var o = a.getAttribute(t) || "";
				o = e + o;
				var s = r.get(o);
				s ? s.push(a) : r.set(o, [a]);
			}
		}
		return r;
	}
	function Hf(e, t, n) {
		e = e.ownerDocument || e, e.head.insertBefore(n, t === "title" ? e.querySelector("head > title") : null);
	}
	function Uf(e, t, n) {
		if (n === 1 || t.itemProp != null) return !1;
		switch (e) {
			case "meta":
			case "title": return !0;
			case "style":
				if (typeof t.precedence != "string" || typeof t.href != "string" || t.href === "") break;
				return !0;
			case "link":
				if (typeof t.rel != "string" || typeof t.href != "string" || t.href === "" || t.onLoad || t.onError) break;
				switch (t.rel) {
					case "stylesheet": return e = t.disabled, typeof t.precedence == "string" && e == null;
					default: return !0;
				}
			case "script": if (t.async && typeof t.async != "function" && typeof t.async != "symbol" && !t.onLoad && !t.onError && t.src && typeof t.src == "string") return !0;
		}
		return !1;
	}
	function Wf(e) {
		return !(e.type === "stylesheet" && !(e.state.loading & 3));
	}
	function Gf(e, t, n, r) {
		if (n.type === "stylesheet" && (typeof r.media != "string" || !1 !== matchMedia(r.media).matches) && !(n.state.loading & 4)) {
			if (n.instance === null) {
				var i = Af(r.href), a = t.querySelector(jf(i));
				if (a) {
					t = a._p, typeof t == "object" && t && typeof t.then == "function" && (e.count++, e = Jf.bind(e), t.then(e, e)), n.state.loading |= 4, n.instance = a, bt(a);
					return;
				}
				a = t.ownerDocument || t, r = Mf(r), (i = mf.get(i)) && Rf(r, i), a = a.createElement("link"), bt(a);
				var o = a;
				o._p = new Promise(function(e, t) {
					o.onload = e, o.onerror = t;
				}), Pd(a, "link", r), n.instance = a;
			}
			e.stylesheets === null && (e.stylesheets = /* @__PURE__ */ new Map()), e.stylesheets.set(n, t), (t = n.state.preload) && !(n.state.loading & 3) && (e.count++, n = Jf.bind(e), t.addEventListener("load", n), t.addEventListener("error", n));
		}
	}
	var Kf = 0;
	function qf(e, t) {
		return e.stylesheets && e.count === 0 && Xf(e, e.stylesheets), 0 < e.count || 0 < e.imgCount ? function(n) {
			var r = setTimeout(function() {
				if (e.stylesheets && Xf(e, e.stylesheets), e.unsuspend) {
					var t = e.unsuspend;
					e.unsuspend = null, t();
				}
			}, 6e4 + t);
			0 < e.imgBytes && Kf === 0 && (Kf = 62500 * Ld());
			var i = setTimeout(function() {
				if (e.waitingForImages = !1, e.count === 0 && (e.stylesheets && Xf(e, e.stylesheets), e.unsuspend)) {
					var t = e.unsuspend;
					e.unsuspend = null, t();
				}
			}, (e.imgBytes > Kf ? 50 : 800) + t);
			return e.unsuspend = n, function() {
				e.unsuspend = null, clearTimeout(r), clearTimeout(i);
			};
		} : null;
	}
	function Jf() {
		if (this.count--, this.count === 0 && (this.imgCount === 0 || !this.waitingForImages)) {
			if (this.stylesheets) Xf(this, this.stylesheets);
			else if (this.unsuspend) {
				var e = this.unsuspend;
				this.unsuspend = null, e();
			}
		}
	}
	var Yf = null;
	function Xf(e, t) {
		e.stylesheets = null, e.unsuspend !== null && (e.count++, Yf = /* @__PURE__ */ new Map(), t.forEach(Zf, e), Yf = null, Jf.call(e));
	}
	function Zf(e, t) {
		if (!(t.state.loading & 4)) {
			var n = Yf.get(e);
			if (n) var r = n.get(null);
			else {
				n = /* @__PURE__ */ new Map(), Yf.set(e, n);
				for (var i = e.querySelectorAll("link[data-precedence],style[data-precedence]"), a = 0; a < i.length; a++) {
					var o = i[a];
					(o.nodeName === "LINK" || o.getAttribute("media") !== "not all") && (n.set(o.dataset.precedence, o), r = o);
				}
				r && n.set(null, r);
			}
			i = t.instance, o = i.getAttribute("data-precedence"), a = n.get(o) || r, a === r && n.set(null, i), n.set(o, i), this.count++, r = Jf.bind(this), i.addEventListener("load", r), i.addEventListener("error", r), a ? a.parentNode.insertBefore(i, a.nextSibling) : (e = e.nodeType === 9 ? e.head : e, e.insertBefore(i, e.firstChild)), t.state.loading |= 4;
		}
	}
	var Qf = {
		$$typeof: C,
		Provider: null,
		Consumer: null,
		_currentValue: re,
		_currentValue2: re,
		_threadCount: 0
	};
	function $f(e, t, n, r, i, a, o, s, c) {
		this.tag = 1, this.containerInfo = e, this.pingCache = this.current = this.pendingChildren = null, this.timeoutHandle = -1, this.callbackNode = this.next = this.pendingContext = this.context = this.cancelPendingCommit = null, this.callbackPriority = 0, this.expirationTimes = Xe(-1), this.entangledLanes = this.shellSuspendCounter = this.errorRecoveryDisabledLanes = this.expiredLanes = this.warmLanes = this.pingedLanes = this.suspendedLanes = this.pendingLanes = 0, this.entanglements = Xe(0), this.hiddenUpdates = Xe(null), this.identifierPrefix = r, this.onUncaughtError = i, this.onCaughtError = a, this.onRecoverableError = o, this.pooledCache = null, this.pooledCacheLanes = 0, this.formState = c, this.incompleteTransitions = /* @__PURE__ */ new Map();
	}
	function ep(e, t, n, r, i, a, o, s, c, l, u, d) {
		return e = new $f(e, t, n, o, c, l, u, d, s), t = 1, !0 === a && (t |= 24), a = ci(3, null, null, t), e.current = a, a.stateNode = e, t = sa(), t.refCount++, e.pooledCache = t, t.refCount++, a.memoizedState = {
			element: r,
			isDehydrated: n,
			cache: t
		}, La(a), e;
	}
	function tp(e) {
		return e ? (e = oi, e) : oi;
	}
	function np(e, t, n, r, i, a) {
		i = tp(i), r.context === null ? r.context = i : r.pendingContext = i, r = za(t), r.payload = { element: n }, a = a === void 0 ? null : a, a !== null && (r.callback = a), n = Ba(e, r, t), n !== null && (hu(n, e, t), Va(n, e, t));
	}
	function rp(e, t) {
		if (e = e.memoizedState, e !== null && e.dehydrated !== null) {
			var n = e.retryLane;
			e.retryLane = n !== 0 && n < t ? n : t;
		}
	}
	function ip(e, t) {
		rp(e, t), (e = e.alternate) && rp(e, t);
	}
	function ap(e) {
		if (e.tag === 13 || e.tag === 31) {
			var t = ri(e, 67108864);
			t !== null && hu(t, e, 67108864), ip(e, 67108864);
		}
	}
	function op(e) {
		if (e.tag === 13 || e.tag === 31) {
			var t = pu();
			t = nt(t);
			var n = ri(e, t);
			n !== null && hu(n, e, t), ip(e, t);
		}
	}
	var sp = !0;
	function cp(e, t, n, r) {
		var i = N.T;
		N.T = null;
		var a = P.p;
		try {
			P.p = 2, up(e, t, n, r);
		} finally {
			P.p = a, N.T = i;
		}
	}
	function lp(e, t, n, r) {
		var i = N.T;
		N.T = null;
		var a = P.p;
		try {
			P.p = 8, up(e, t, n, r);
		} finally {
			P.p = a, N.T = i;
		}
	}
	function up(e, t, n, r) {
		if (sp) {
			var i = dp(r);
			if (i === null) wd(e, t, r, fp, n), Cp(e, r);
			else if (Tp(i, e, t, n, r)) r.stopPropagation();
			else if (Cp(e, r), t & 4 && -1 < Sp.indexOf(e)) {
				for (; i !== null;) {
					var a = _t(i);
					if (a !== null) switch (a.tag) {
						case 3:
							if (a = a.stateNode, a.current.memoizedState.isDehydrated) {
								var o = Ge(a.pendingLanes);
								if (o !== 0) {
									var s = a;
									for (s.pendingLanes |= 2, s.entangledLanes |= 2; o;) {
										var c = 1 << 31 - Re(o);
										s.entanglements[1] |= c, o &= ~c;
									}
									rd(a), !(K & 6) && (tu = Ee() + 500, id(0, !1));
								}
							}
							break;
						case 31:
						case 13: s = ri(a, 2), s !== null && hu(s, a, 2), bu(), ip(a, 2);
					}
					if (a = dp(r), a === null && wd(e, t, r, fp, n), a === i) break;
					i = a;
				}
				i !== null && r.stopPropagation();
			} else wd(e, t, r, null, n);
		}
	}
	function dp(e) {
		return e = nn(e), pp(e);
	}
	var fp = null;
	function pp(e) {
		if (fp = null, e = gt(e), e !== null) {
			var t = l(e);
			if (t === null) e = null;
			else {
				var n = t.tag;
				if (n === 13) {
					if (e = u(t), e !== null) return e;
					e = null;
				} else if (n === 31) {
					if (e = d(t), e !== null) return e;
					e = null;
				} else if (n === 3) {
					if (t.stateNode.current.memoizedState.isDehydrated) return t.tag === 3 ? t.stateNode.containerInfo : null;
					e = null;
				} else t !== e && (e = null);
			}
		}
		return fp = e, null;
	}
	function mp(e) {
		switch (e) {
			case "beforetoggle":
			case "cancel":
			case "click":
			case "close":
			case "contextmenu":
			case "copy":
			case "cut":
			case "auxclick":
			case "dblclick":
			case "dragend":
			case "dragstart":
			case "drop":
			case "focusin":
			case "focusout":
			case "input":
			case "invalid":
			case "keydown":
			case "keypress":
			case "keyup":
			case "mousedown":
			case "mouseup":
			case "paste":
			case "pause":
			case "play":
			case "pointercancel":
			case "pointerdown":
			case "pointerup":
			case "ratechange":
			case "reset":
			case "resize":
			case "seeked":
			case "submit":
			case "toggle":
			case "touchcancel":
			case "touchend":
			case "touchstart":
			case "volumechange":
			case "change":
			case "selectionchange":
			case "textInput":
			case "compositionstart":
			case "compositionend":
			case "compositionupdate":
			case "beforeblur":
			case "afterblur":
			case "beforeinput":
			case "blur":
			case "fullscreenchange":
			case "focus":
			case "hashchange":
			case "popstate":
			case "select":
			case "selectstart": return 2;
			case "drag":
			case "dragenter":
			case "dragexit":
			case "dragleave":
			case "dragover":
			case "mousemove":
			case "mouseout":
			case "mouseover":
			case "pointermove":
			case "pointerout":
			case "pointerover":
			case "scroll":
			case "touchmove":
			case "wheel":
			case "mouseenter":
			case "mouseleave":
			case "pointerenter":
			case "pointerleave": return 8;
			case "message": switch (De()) {
				case Oe: return 2;
				case ke: return 8;
				case Ae:
				case je: return 32;
				case Me: return 268435456;
				default: return 32;
			}
			default: return 32;
		}
	}
	var hp = !1, gp = null, _p = null, vp = null, yp = /* @__PURE__ */ new Map(), bp = /* @__PURE__ */ new Map(), xp = [], Sp = "mousedown mouseup touchcancel touchend touchstart auxclick dblclick pointercancel pointerdown pointerup dragend dragstart drop compositionend compositionstart keydown keypress keyup input textInput copy cut paste click change contextmenu reset".split(" ");
	function Cp(e, t) {
		switch (e) {
			case "focusin":
			case "focusout":
				gp = null;
				break;
			case "dragenter":
			case "dragleave":
				_p = null;
				break;
			case "mouseover":
			case "mouseout":
				vp = null;
				break;
			case "pointerover":
			case "pointerout":
				yp.delete(t.pointerId);
				break;
			case "gotpointercapture":
			case "lostpointercapture": bp.delete(t.pointerId);
		}
	}
	function wp(e, t, n, r, i, a) {
		return e === null || e.nativeEvent !== a ? (e = {
			blockedOn: t,
			domEventName: n,
			eventSystemFlags: r,
			nativeEvent: a,
			targetContainers: [i]
		}, t !== null && (t = _t(t), t !== null && ap(t)), e) : (e.eventSystemFlags |= r, t = e.targetContainers, i !== null && t.indexOf(i) === -1 && t.push(i), e);
	}
	function Tp(e, t, n, r, i) {
		switch (t) {
			case "focusin": return gp = wp(gp, e, t, n, r, i), !0;
			case "dragenter": return _p = wp(_p, e, t, n, r, i), !0;
			case "mouseover": return vp = wp(vp, e, t, n, r, i), !0;
			case "pointerover":
				var a = i.pointerId;
				return yp.set(a, wp(yp.get(a) || null, e, t, n, r, i)), !0;
			case "gotpointercapture": return a = i.pointerId, bp.set(a, wp(bp.get(a) || null, e, t, n, r, i)), !0;
		}
		return !1;
	}
	function Ep(e) {
		var t = gt(e.target);
		if (t !== null) {
			var n = l(t);
			if (n !== null) {
				if (t = n.tag, t === 13) {
					if (t = u(n), t !== null) {
						e.blockedOn = t, at(e.priority, function() {
							op(n);
						});
						return;
					}
				} else if (t === 31) {
					if (t = d(n), t !== null) {
						e.blockedOn = t, at(e.priority, function() {
							op(n);
						});
						return;
					}
				} else if (t === 3 && n.stateNode.current.memoizedState.isDehydrated) {
					e.blockedOn = n.tag === 3 ? n.stateNode.containerInfo : null;
					return;
				}
			}
		}
		e.blockedOn = null;
	}
	function Dp(e) {
		if (e.blockedOn !== null) return !1;
		for (var t = e.targetContainers; 0 < t.length;) {
			var n = dp(e.nativeEvent);
			if (n === null) {
				n = e.nativeEvent;
				var r = new n.constructor(n.type, n);
				tn = r, n.target.dispatchEvent(r), tn = null;
			} else return t = _t(n), t !== null && ap(t), e.blockedOn = n, !1;
			t.shift();
		}
		return !0;
	}
	function Op(e, t, n) {
		Dp(e) && n.delete(t);
	}
	function kp() {
		hp = !1, gp !== null && Dp(gp) && (gp = null), _p !== null && Dp(_p) && (_p = null), vp !== null && Dp(vp) && (vp = null), yp.forEach(Op), bp.forEach(Op);
	}
	function Ap(e, n) {
		e.blockedOn === n && (e.blockedOn = null, hp || (hp = !0, t.unstable_scheduleCallback(t.unstable_NormalPriority, kp)));
	}
	var jp = null;
	function Mp(e) {
		jp !== e && (jp = e, t.unstable_scheduleCallback(t.unstable_NormalPriority, function() {
			jp === e && (jp = null);
			for (var t = 0; t < e.length; t += 3) {
				var n = e[t], r = e[t + 1], i = e[t + 2];
				if (typeof r != "function") {
					if (pp(r || n) === null) continue;
					break;
				}
				var a = _t(n);
				a !== null && (e.splice(t, 3), t -= 3, Ss(a, {
					pending: !0,
					data: i,
					method: n.method,
					action: r
				}, r, i));
			}
		}));
	}
	function Np(e) {
		function t(t) {
			return Ap(t, e);
		}
		gp !== null && Ap(gp, e), _p !== null && Ap(_p, e), vp !== null && Ap(vp, e), yp.forEach(t), bp.forEach(t);
		for (var n = 0; n < xp.length; n++) {
			var r = xp[n];
			r.blockedOn === e && (r.blockedOn = null);
		}
		for (; 0 < xp.length && (n = xp[0], n.blockedOn === null);) Ep(n), n.blockedOn === null && xp.shift();
		if (n = (e.ownerDocument || e).$$reactFormReplay, n != null) for (r = 0; r < n.length; r += 3) {
			var i = n[r], a = n[r + 1], o = i[ct] || null;
			if (typeof a == "function") o || Mp(n);
			else if (o) {
				var s = null;
				if (a && a.hasAttribute("formAction")) {
					if (i = a, o = a[ct] || null) s = o.formAction;
					else if (pp(i) !== null) continue;
				} else s = o.action;
				typeof s == "function" ? n[r + 1] = s : (n.splice(r, 3), r -= 3), Mp(n);
			}
		}
	}
	function Pp() {
		function e(e) {
			e.canIntercept && e.info === "react-transition" && e.intercept({
				handler: function() {
					return new Promise(function(e) {
						return i = e;
					});
				},
				focusReset: "manual",
				scroll: "manual"
			});
		}
		function t() {
			i !== null && (i(), i = null), r || setTimeout(n, 20);
		}
		function n() {
			if (!r && !navigation.transition) {
				var e = navigation.currentEntry;
				e && e.url != null && navigation.navigate(e.url, {
					state: e.getState(),
					info: "react-transition",
					history: "replace"
				});
			}
		}
		if (typeof navigation == "object") {
			var r = !1, i = null;
			return navigation.addEventListener("navigate", e), navigation.addEventListener("navigatesuccess", t), navigation.addEventListener("navigateerror", t), setTimeout(n, 100), function() {
				r = !0, navigation.removeEventListener("navigate", e), navigation.removeEventListener("navigatesuccess", t), navigation.removeEventListener("navigateerror", t), i !== null && (i(), i = null);
			};
		}
	}
	function Fp(e) {
		this._internalRoot = e;
	}
	Ip.prototype.render = Fp.prototype.render = function(e) {
		var t = this._internalRoot;
		if (t === null) throw Error(a(409));
		var n = t.current;
		np(n, pu(), e, t, null, null);
	}, Ip.prototype.unmount = Fp.prototype.unmount = function() {
		var e = this._internalRoot;
		if (e !== null) {
			this._internalRoot = null;
			var t = e.containerInfo;
			np(e.current, 2, null, e, null, null), bu(), t[lt] = null;
		}
	};
	function Ip(e) {
		this._internalRoot = e;
	}
	Ip.prototype.unstable_scheduleHydration = function(e) {
		if (e) {
			var t = it();
			e = {
				blockedOn: null,
				target: e,
				priority: t
			};
			for (var n = 0; n < xp.length && t !== 0 && t < xp[n].priority; n++);
			xp.splice(n, 0, e), n === 0 && Ep(e);
		}
	};
	var Lp = n.version;
	if (Lp !== "19.2.8") throw Error(a(527, Lp, "19.2.8"));
	P.findDOMNode = function(e) {
		var t = e._reactInternals;
		if (t === void 0) throw typeof e.render == "function" ? Error(a(188)) : (e = Object.keys(e).join(","), Error(a(268, e)));
		return e = p(t), e = e === null ? null : m(e), e = e === null ? null : e.stateNode, e;
	};
	var Rp = {
		bundleType: 0,
		version: "19.2.8",
		rendererPackageName: "react-dom",
		currentDispatcherRef: N,
		reconcilerVersion: "19.2.8"
	};
	if (typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ < "u") {
		var zp = __REACT_DEVTOOLS_GLOBAL_HOOK__;
		if (!zp.isDisabled && zp.supportsFiber) try {
			Fe = zp.inject(Rp), Ie = zp;
		} catch {}
	}
	e.createRoot = function(e, t) {
		if (!s(e)) throw Error(a(299));
		var n = !1, r = "", i = Gs, o = Ks, c = qs;
		return t != null && (!0 === t.unstable_strictMode && (n = !0), t.identifierPrefix !== void 0 && (r = t.identifierPrefix), t.onUncaughtError !== void 0 && (i = t.onUncaughtError), t.onCaughtError !== void 0 && (o = t.onCaughtError), t.onRecoverableError !== void 0 && (c = t.onRecoverableError)), t = ep(e, 1, !1, null, null, n, r, null, i, o, c, Pp), e[lt] = t.current, Sd(e), new Fp(t);
	};
})), u = /* @__PURE__ */ t(((e, t) => {
	function n() {
		if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function")) try {
			__REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(n);
		} catch (e) {
			console.error(e);
		}
	}
	n(), t.exports = l();
})), d = /* @__PURE__ */ t(((e) => {
	var t = Symbol.for("react.transitional.element"), n = Symbol.for("react.fragment");
	function r(e, n, r) {
		var i = null;
		if (r !== void 0 && (i = "" + r), n.key !== void 0 && (i = "" + n.key), "key" in n) for (var a in r = {}, n) a !== "key" && (r[a] = n[a]);
		else r = n;
		return n = r.ref, {
			$$typeof: t,
			type: e,
			key: i,
			ref: n === void 0 ? null : n,
			props: r
		};
	}
	e.Fragment = n, e.jsx = r, e.jsxs = r;
})), f = /* @__PURE__ */ t(((e, t) => {
	t.exports = d();
})), p = u(), m = f(), h = i();
function g(e, t) {
	var n = Object.keys(e);
	if (Object.getOwnPropertySymbols) {
		var r = Object.getOwnPropertySymbols(e);
		t && (r = r.filter((function(t) {
			return Object.getOwnPropertyDescriptor(e, t).enumerable;
		}))), n.push.apply(n, r);
	}
	return n;
}
function _(e) {
	for (var t = 1; t < arguments.length; t++) {
		var n = arguments[t] == null ? {} : arguments[t];
		t % 2 ? g(Object(n), !0).forEach((function(t) {
			v(e, t, n[t]);
		})) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : g(Object(n)).forEach((function(t) {
			Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t));
		}));
	}
	return e;
}
function v(e, t, n) {
	return (t = function(e) {
		var t = function(e, t) {
			if (typeof e != "object" || !e) return e;
			var n = e[Symbol.toPrimitive];
			if (n !== void 0) {
				var r = n.call(e, t || "default");
				if (typeof r != "object") return r;
				throw TypeError("@@toPrimitive must return a primitive value.");
			}
			return (t === "string" ? String : Number)(e);
		}(e, "string");
		return typeof t == "symbol" ? t : String(t);
	}(t)) in e ? Object.defineProperty(e, t, {
		value: n,
		enumerable: !0,
		configurable: !0,
		writable: !0
	}) : e[t] = n, e;
}
function y(e, t) {
	if (e == null) return {};
	var n, r, i = function(e, t) {
		if (e == null) return {};
		var n, r, i = {}, a = Object.keys(e);
		for (r = 0; r < a.length; r++) n = a[r], t.indexOf(n) >= 0 || (i[n] = e[n]);
		return i;
	}(e, t);
	if (Object.getOwnPropertySymbols) {
		var a = Object.getOwnPropertySymbols(e);
		for (r = 0; r < a.length; r++) n = a[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (i[n] = e[n]);
	}
	return i;
}
function b(e, t) {
	return C(e) || function(e, t) {
		var n = e == null ? null : typeof Symbol < "u" && e[Symbol.iterator] || e["@@iterator"];
		if (n != null) {
			var r, i, a, o, s = [], c = !0, l = !1;
			try {
				if (a = (n = n.call(e)).next, t === 0) {
					if (Object(n) !== n) return;
					c = !1;
				} else for (; !(c = (r = a.call(n)).done) && (s.push(r.value), s.length !== t); c = !0);
			} catch (e) {
				l = !0, i = e;
			} finally {
				try {
					if (!c && n.return != null && (o = n.return(), Object(o) !== o)) return;
				} finally {
					if (l) throw i;
				}
			}
			return s;
		}
	}(e, t) || T(e, t) || D();
}
function x(e) {
	return C(e) || w(e) || T(e) || D();
}
function S(e) {
	return function(e) {
		if (Array.isArray(e)) return E(e);
	}(e) || w(e) || T(e) || function() {
		throw TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
	}();
}
function C(e) {
	if (Array.isArray(e)) return e;
}
function w(e) {
	if (typeof Symbol < "u" && e[Symbol.iterator] != null || e["@@iterator"] != null) return Array.from(e);
}
function T(e, t) {
	if (e) {
		if (typeof e == "string") return E(e, t);
		var n = Object.prototype.toString.call(e).slice(8, -1);
		return n === "Object" && e.constructor && (n = e.constructor.name), n === "Map" || n === "Set" ? Array.from(e) : n === "Arguments" || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n) ? E(e, t) : void 0;
	}
}
function E(e, t) {
	(t == null || t > e.length) && (t = e.length);
	for (var n = 0, r = Array(t); n < t; n++) r[n] = e[n];
	return r;
}
function D() {
	throw TypeError("Invalid attempt to destructure non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
}
function O(e, t) {
	var n = typeof Symbol < "u" && e[Symbol.iterator] || e["@@iterator"];
	if (!n) {
		if (Array.isArray(e) || (n = T(e)) || t && e && typeof e.length == "number") {
			n && (e = n);
			var r = 0, i = function() {};
			return {
				s: i,
				n: function() {
					return r >= e.length ? { done: !0 } : {
						done: !1,
						value: e[r++]
					};
				},
				e: function(e) {
					throw e;
				},
				f: i
			};
		}
		throw TypeError("Invalid attempt to iterate non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
	}
	var a, o = !0, s = !1;
	return {
		s: function() {
			n = n.call(e);
		},
		n: function() {
			var e = n.next();
			return o = e.done, e;
		},
		e: function(e) {
			s = !0, a = e;
		},
		f: function() {
			try {
				o || n.return == null || n.return();
			} finally {
				if (s) throw a;
			}
		}
	};
}
var k = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : typeof global < "u" ? global : typeof self < "u" ? self : {};
function A(e, t) {
	return e(t = { exports: {} }, t.exports), t.exports;
}
var j = A((function(e) {
	(function() {
		var t = {}.hasOwnProperty;
		function n() {
			for (var e = [], r = 0; r < arguments.length; r++) {
				var i = arguments[r];
				if (i) {
					var a = typeof i;
					if (a === "string" || a === "number") e.push(i);
					else if (Array.isArray(i)) {
						if (i.length) {
							var o = n.apply(null, i);
							o && e.push(o);
						}
					} else if (a === "object") {
						if (i.toString !== Object.prototype.toString && !i.toString.toString().includes("[native code]")) {
							e.push(i.toString());
							continue;
						}
						for (var s in i) t.call(i, s) && i[s] && e.push(s);
					}
				}
			}
			return e.join(" ");
		}
		e.exports ? (n.default = n, e.exports = n) : window.classNames = n;
	})();
})), M = {
	hunkClassName: "",
	lineClassName: "",
	gutterClassName: "",
	codeClassName: "",
	monotonous: !1,
	gutterType: "default",
	viewType: "split",
	widgets: {},
	hideGutter: !1,
	selectedChanges: [],
	generateAnchorID: function() {},
	generateLineClassName: function() {},
	renderGutter: function(e) {
		var t = e.renderDefault;
		return (0, e.wrapInAnchor)(t());
	},
	codeEvents: {},
	gutterEvents: {}
}, ee = (0, h.createContext)(M), te = ee.Provider, ne = function() {
	return (0, h.useContext)(ee);
}, N = A((function(e, t) {
	(function(t) {
		function n(e) {
			var t = e.slice(11), n = null, r = null;
			switch (t.indexOf("\"")) {
				case -1:
					n = (o = t.split(" "))[0].slice(2), r = o[1].slice(2);
					break;
				case 0:
					var i = t.indexOf("\"", 2);
					n = t.slice(3, i);
					var a = t.indexOf("\"", i + 1);
					r = a < 0 ? t.slice(i + 4) : t.slice(a + 3, -1);
					break;
				default:
					var o;
					n = (o = t.split(" "))[0].slice(2), r = o[1].slice(3, -1);
			}
			return {
				oldPath: n,
				newPath: r
			};
		}
		e.exports = { parse: function(e) {
			for (var t, r, i, a, o, s = [], c = 2, l = e.split("\n"), u = l.length, d = 0; d < u;) {
				var f = l[d];
				if (f.indexOf("diff --git") === 0) {
					t = {
						hunks: [],
						oldEndingNewLine: !0,
						newEndingNewLine: !0,
						oldPath: (o = n(f)).oldPath,
						newPath: o.newPath
					}, s.push(t);
					var p, m = null;
					simiLoop: for (; p = l[++d];) {
						var h = p.indexOf(" "), g = h > -1 ? p.slice(0, h) : g;
						switch (g) {
							case "diff":
								d--;
								break simiLoop;
							case "deleted":
							case "new":
								var _ = p.slice(h + 1);
								_.indexOf("file mode") === 0 && (t[g === "new" ? "newMode" : "oldMode"] = _.slice(10));
								break;
							case "similarity":
								t.similarity = parseInt(p.split(" ")[2], 10);
								break;
							case "index":
								var v = p.slice(h + 1).split(" "), y = v[0].split("..");
								t.oldRevision = y[0], t.newRevision = y[1], v[1] && (t.oldMode = t.newMode = v[1]);
								break;
							case "copy":
							case "rename":
								var b = p.slice(h + 1);
								b.indexOf("from") === 0 ? t.oldPath = b.slice(5) : t.newPath = b.slice(3), m = g;
								break;
							case "---":
								var x = p.slice(h + 1), S = l[++d].slice(4);
								x === "/dev/null" ? (S = S.slice(2), m = "add") : S === "/dev/null" ? (x = x.slice(2), m = "delete") : (m = "modify", x = x.slice(2), S = S.slice(2)), x && (t.oldPath = x), S && (t.newPath = S), c = 5;
								break simiLoop;
						}
					}
					t.type = m || "modify";
				} else if (f.indexOf("Binary") === 0) t.isBinary = !0, t.type = f.indexOf("/dev/null and") >= 0 ? "add" : f.indexOf("and /dev/null") >= 0 ? "delete" : "modify", c = 2, t = null;
				else if (c === 5) {
					if (f.indexOf("@@") === 0) {
						var C = /^@@\s+-([0-9]+)(,([0-9]+))?\s+\+([0-9]+)(,([0-9]+))?/.exec(f);
						r = {
							content: f,
							oldStart: C[1] - 0,
							newStart: C[4] - 0,
							oldLines: C[3] - 0 || 1,
							newLines: C[6] - 0 || 1,
							changes: []
						}, t.hunks.push(r), i = r.oldStart, a = r.newStart;
					} else {
						var w = f.slice(0, 1), T = { content: f.slice(1) };
						switch (w) {
							case "+":
								T.type = "insert", T.isInsert = !0, T.lineNumber = a, a++;
								break;
							case "-":
								T.type = "delete", T.isDelete = !0, T.lineNumber = i, i++;
								break;
							case " ":
								T.type = "normal", T.isNormal = !0, T.oldLineNumber = i, T.newLineNumber = a, i++, a++;
								break;
							case "\\":
								var E = r.changes[r.changes.length - 1];
								E.isDelete || (t.newEndingNewLine = !1), E.isInsert || (t.oldEndingNewLine = !1);
						}
						T.type && r.changes.push(T);
					}
				}
				d++;
			}
			return s;
		} };
	})();
}));
function P(e) {
	return e.type === "insert";
}
function re(e) {
	return e.type === "delete";
}
function ie(e) {
	return e.type === "normal";
}
function ae(e, t) {
	var n = t.nearbySequences === "zip" ? function(e) {
		return b(e.reduce((function(e, t, n) {
			var r = b(e, 3), i = r[0], a = r[1], o = r[2];
			return a ? P(t) && o >= 0 ? (i.splice(o + 1, 0, t), [
				i,
				t,
				o + 2
			]) : (i.push(t), [
				i,
				t,
				re(t) && re(a) ? o : n
			]) : (i.push(t), [
				i,
				t,
				re(t) ? n : -1
			]);
		}), [
			[],
			null,
			-1
		]), 1)[0];
	}(e.changes) : e.changes;
	return _(_({}, e), {}, {
		isPlain: !1,
		changes: n
	});
}
function oe(e) {
	var t = arguments.length > 1 && arguments[1] !== void 0 ? arguments[1] : {}, n = function(e) {
		if (e.startsWith("diff --git")) return e;
		var t = e.indexOf("\n"), n = e.indexOf("\n", t + 1), r = e.slice(0, t), i = e.slice(t + 1, n), a = r.split(" ").slice(1, -3).join(" "), o = i.split(" ").slice(1, -3).join(" ");
		return [
			`diff --git a/${a} b/${o}`,
			"index 1111111..2222222 100644",
			`--- a/${a}`,
			`+++ b/${o}`,
			e.slice(n + 1)
		].join("\n");
	}(e.trimStart());
	return N.parse(n).map((function(e) {
		return function(e, t) {
			var n = e.hunks.map((function(e) {
				return ae(e, t);
			}));
			return _(_({}, e), {}, { hunks: n });
		}(e, t);
	}));
}
function F(e) {
	return e[0];
}
function I(e) {
	return e[e.length - 1];
}
function L(e) {
	return [`${e}Start`, `${e}Lines`];
}
function se(e) {
	return e === "old" ? function(e) {
		return P(e) ? -1 : ie(e) ? e.oldLineNumber : e.lineNumber;
	} : function(e) {
		return re(e) ? -1 : ie(e) ? e.newLineNumber : e.lineNumber;
	};
}
function ce(e, t) {
	return function(n, r) {
		var i = n[e], a = i + n[t];
		return r >= i && r < a;
	};
}
function le(e, t) {
	return function(n, r, i) {
		var a = n[e] + n[t], o = r[e];
		return i >= a && i < o;
	};
}
function ue(e) {
	var t = se(e), n = function(e) {
		var t = b(L(e), 2), n = ce(t[0], t[1]);
		return function(e, t) {
			return e.find((function(e) {
				return n(e, t);
			}));
		};
	}(e);
	return function(e, r) {
		var i = n(e, r);
		if (i) return i.changes.find((function(e) {
			return t(e) === r;
		}));
	};
}
function de(e) {
	var t = e === "old" ? "new" : "old", n = b(L(e), 2), r = n[0], i = n[1], a = b(L(t), 2), o = a[0], s = a[1], c = se(e), l = se(t), u = ce(r, i), d = le(r, i);
	return function(e, t) {
		var n = F(e);
		if (t < n[r]) {
			var a = n[r] - t;
			return n[o] - a;
		}
		var f = I(e);
		if (f[r] + f[i] <= t) {
			var p = t - f[r] - f[i];
			return f[o] + f[s] + p;
		}
		for (var m = 0; m < e.length; m++) {
			var h = e[m], g = e[m + 1];
			if (u(h, t)) {
				var _ = h.changes.findIndex((function(e) {
					return c(e) === t;
				})), v = h.changes[_];
				if (ie(v)) return l(v);
				var y = re(v) ? _ + 1 : _ - 1, b = h.changes[y];
				if (!b) return -1;
				var x = P(v) ? "delete" : "insert";
				return b.type === x ? l(b) : -1;
			}
			if (d(h, g, t)) {
				var S = t - h[r] - h[i];
				return h[o] + h[s] + S;
			}
		}
		throw Error(`Unexpected line position ${t}`);
	};
}
var fe = function(e, t, n, r) {
	for (var i = e.length, a = n + (r ? 1 : -1); r ? a-- : ++a < i;) if (t(e[a], a, e)) return a;
	return -1;
}, pe = function() {
	this.__data__ = [], this.size = 0;
}, me = function(e, t) {
	return e === t || e != e && t != t;
}, he = function(e, t) {
	for (var n = e.length; n--;) if (me(e[n][0], t)) return n;
	return -1;
}, ge = Array.prototype.splice, _e = function(e) {
	var t = this.__data__, n = he(t, e);
	return !(n < 0) && (n == t.length - 1 ? t.pop() : ge.call(t, n, 1), --this.size, !0);
}, ve = function(e) {
	var t = this.__data__, n = he(t, e);
	return n < 0 ? void 0 : t[n][1];
}, ye = function(e) {
	return he(this.__data__, e) > -1;
}, be = function(e, t) {
	var n = this.__data__, r = he(n, e);
	return r < 0 ? (++this.size, n.push([e, t])) : n[r][1] = t, this;
};
function xe(e) {
	var t = -1, n = e == null ? 0 : e.length;
	for (this.clear(); ++t < n;) {
		var r = e[t];
		this.set(r[0], r[1]);
	}
}
xe.prototype.clear = pe, xe.prototype.delete = _e, xe.prototype.get = ve, xe.prototype.has = ye, xe.prototype.set = be;
var Se = xe, Ce = function() {
	this.__data__ = new Se(), this.size = 0;
}, we = function(e) {
	var t = this.__data__, n = t.delete(e);
	return this.size = t.size, n;
}, Te = function(e) {
	return this.__data__.get(e);
}, Ee = function(e) {
	return this.__data__.has(e);
}, De = typeof k == "object" && k && k.Object === Object && k, Oe = typeof self == "object" && self && self.Object === Object && self, ke = De || Oe || Function("return this")(), Ae = ke.Symbol, je = Object.prototype, Me = je.hasOwnProperty, Ne = je.toString, Pe = Ae ? Ae.toStringTag : void 0, Fe = function(e) {
	var t = Me.call(e, Pe), n = e[Pe];
	try {
		e[Pe] = void 0;
		var r = !0;
	} catch {}
	var i = Ne.call(e);
	return r && (t ? e[Pe] = n : delete e[Pe]), i;
}, Ie = Object.prototype.toString, Le = function(e) {
	return Ie.call(e);
}, Re = Ae ? Ae.toStringTag : void 0, ze = function(e) {
	return e == null ? e === void 0 ? "[object Undefined]" : "[object Null]" : Re && Re in Object(e) ? Fe(e) : Le(e);
}, Be = function(e) {
	var t = typeof e;
	return e != null && (t == "object" || t == "function");
}, Ve = function(e) {
	if (!Be(e)) return !1;
	var t = ze(e);
	return t == "[object Function]" || t == "[object GeneratorFunction]" || t == "[object AsyncFunction]" || t == "[object Proxy]";
}, He = ke["__core-js_shared__"], Ue = function() {
	var e = /[^.]+$/.exec(He && He.keys && He.keys.IE_PROTO || "");
	return e ? "Symbol(src)_1." + e : "";
}(), We = function(e) {
	return !!Ue && Ue in e;
}, Ge = Function.prototype.toString, Ke = function(e) {
	if (e != null) {
		try {
			return Ge.call(e);
		} catch {}
		try {
			return e + "";
		} catch {}
	}
	return "";
}, qe = /^\[object .+?Constructor\]$/, Je = Function.prototype, Ye = Object.prototype, Xe = Je.toString, Ze = Ye.hasOwnProperty, Qe = RegExp("^" + Xe.call(Ze).replace(/[\\^$.*+?()[\]{}|]/g, "\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g, "$1.*?") + "$"), $e = function(e) {
	return !(!Be(e) || We(e)) && (Ve(e) ? Qe : qe).test(Ke(e));
}, et = function(e, t) {
	return e?.[t];
}, tt = function(e, t) {
	var n = et(e, t);
	return $e(n) ? n : void 0;
}, nt = tt(ke, "Map"), rt = tt(Object, "create"), it = function() {
	this.__data__ = rt ? rt(null) : {}, this.size = 0;
}, at = function(e) {
	var t = this.has(e) && delete this.__data__[e];
	return this.size -= +!!t, t;
}, ot = Object.prototype.hasOwnProperty, st = function(e) {
	var t = this.__data__;
	if (rt) {
		var n = t[e];
		return n === "__lodash_hash_undefined__" ? void 0 : n;
	}
	return ot.call(t, e) ? t[e] : void 0;
}, ct = Object.prototype.hasOwnProperty, lt = function(e) {
	var t = this.__data__;
	return rt ? t[e] !== void 0 : ct.call(t, e);
}, ut = function(e, t) {
	var n = this.__data__;
	return this.size += +!this.has(e), n[e] = rt && t === void 0 ? "__lodash_hash_undefined__" : t, this;
};
function dt(e) {
	var t = -1, n = e == null ? 0 : e.length;
	for (this.clear(); ++t < n;) {
		var r = e[t];
		this.set(r[0], r[1]);
	}
}
dt.prototype.clear = it, dt.prototype.delete = at, dt.prototype.get = st, dt.prototype.has = lt, dt.prototype.set = ut;
var ft = dt, pt = function() {
	this.size = 0, this.__data__ = {
		hash: new ft(),
		map: new (nt || Se)(),
		string: new ft()
	};
}, mt = function(e) {
	var t = typeof e;
	return t == "string" || t == "number" || t == "symbol" || t == "boolean" ? e !== "__proto__" : e === null;
}, ht = function(e, t) {
	var n = e.__data__;
	return mt(t) ? n[typeof t == "string" ? "string" : "hash"] : n.map;
}, gt = function(e) {
	var t = ht(this, e).delete(e);
	return this.size -= +!!t, t;
}, _t = function(e) {
	return ht(this, e).get(e);
}, vt = function(e) {
	return ht(this, e).has(e);
}, yt = function(e, t) {
	var n = ht(this, e), r = n.size;
	return n.set(e, t), this.size += n.size == r ? 0 : 1, this;
};
function bt(e) {
	var t = -1, n = e == null ? 0 : e.length;
	for (this.clear(); ++t < n;) {
		var r = e[t];
		this.set(r[0], r[1]);
	}
}
bt.prototype.clear = pt, bt.prototype.delete = gt, bt.prototype.get = _t, bt.prototype.has = vt, bt.prototype.set = yt;
var xt = bt, St = function(e, t) {
	var n = this.__data__;
	if (n instanceof Se) {
		var r = n.__data__;
		if (!nt || r.length < 199) return r.push([e, t]), this.size = ++n.size, this;
		n = this.__data__ = new xt(r);
	}
	return n.set(e, t), this.size = n.size, this;
};
function Ct(e) {
	var t = this.__data__ = new Se(e);
	this.size = t.size;
}
Ct.prototype.clear = Ce, Ct.prototype.delete = we, Ct.prototype.get = Te, Ct.prototype.has = Ee, Ct.prototype.set = St;
var wt = Ct, Tt = function(e) {
	return this.__data__.set(e, "__lodash_hash_undefined__"), this;
}, Et = function(e) {
	return this.__data__.has(e);
};
function Dt(e) {
	var t = -1, n = e == null ? 0 : e.length;
	for (this.__data__ = new xt(); ++t < n;) this.add(e[t]);
}
Dt.prototype.add = Dt.prototype.push = Tt, Dt.prototype.has = Et;
var Ot = Dt, kt = function(e, t) {
	for (var n = -1, r = e == null ? 0 : e.length; ++n < r;) if (t(e[n], n, e)) return !0;
	return !1;
}, At = function(e, t) {
	return e.has(t);
}, jt = function(e, t, n, r, i, a) {
	var o = 1 & n, s = e.length, c = t.length;
	if (s != c && !(o && c > s)) return !1;
	var l = a.get(e), u = a.get(t);
	if (l && u) return l == t && u == e;
	var d = -1, f = !0, p = 2 & n ? new Ot() : void 0;
	for (a.set(e, t), a.set(t, e); ++d < s;) {
		var m = e[d], h = t[d];
		if (r) var g = o ? r(h, m, d, t, e, a) : r(m, h, d, e, t, a);
		if (g !== void 0) {
			if (g) continue;
			f = !1;
			break;
		}
		if (p) {
			if (!kt(t, (function(e, t) {
				if (!At(p, t) && (m === e || i(m, e, n, r, a))) return p.push(t);
			}))) {
				f = !1;
				break;
			}
		} else if (m !== h && !i(m, h, n, r, a)) {
			f = !1;
			break;
		}
	}
	return a.delete(e), a.delete(t), f;
}, Mt = ke.Uint8Array, Nt = function(e) {
	var t = -1, n = Array(e.size);
	return e.forEach((function(e, r) {
		n[++t] = [r, e];
	})), n;
}, Pt = function(e) {
	var t = -1, n = Array(e.size);
	return e.forEach((function(e) {
		n[++t] = e;
	})), n;
}, Ft = Ae ? Ae.prototype : void 0, It = Ft ? Ft.valueOf : void 0, Lt = function(e, t, n, r, i, a, o) {
	switch (n) {
		case "[object DataView]":
			if (e.byteLength != t.byteLength || e.byteOffset != t.byteOffset) return !1;
			e = e.buffer, t = t.buffer;
		case "[object ArrayBuffer]": return !(e.byteLength != t.byteLength || !a(new Mt(e), new Mt(t)));
		case "[object Boolean]":
		case "[object Date]":
		case "[object Number]": return me(+e, +t);
		case "[object Error]": return e.name == t.name && e.message == t.message;
		case "[object RegExp]":
		case "[object String]": return e == t + "";
		case "[object Map]": var s = Nt;
		case "[object Set]":
			var c = 1 & r;
			if (s ||= Pt, e.size != t.size && !c) return !1;
			var l = o.get(e);
			if (l) return l == t;
			r |= 2, o.set(e, t);
			var u = jt(s(e), s(t), r, i, a, o);
			return o.delete(e), u;
		case "[object Symbol]": if (It) return It.call(e) == It.call(t);
	}
	return !1;
}, Rt = function(e, t) {
	for (var n = -1, r = t.length, i = e.length; ++n < r;) e[i + n] = t[n];
	return e;
}, zt = Array.isArray, Bt = function(e, t, n) {
	var r = t(e);
	return zt(e) ? r : Rt(r, n(e));
}, Vt = function(e, t) {
	for (var n = -1, r = e == null ? 0 : e.length, i = 0, a = []; ++n < r;) {
		var o = e[n];
		t(o, n, e) && (a[i++] = o);
	}
	return a;
}, Ht = function() {
	return [];
}, Ut = Object.prototype.propertyIsEnumerable, Wt = Object.getOwnPropertySymbols, Gt = Wt ? function(e) {
	return e == null ? [] : (e = Object(e), Vt(Wt(e), (function(t) {
		return Ut.call(e, t);
	})));
} : Ht, Kt = function(e, t) {
	for (var n = -1, r = Array(e); ++n < e;) r[n] = t(n);
	return r;
}, qt = function(e) {
	return typeof e == "object" && !!e;
}, Jt = function(e) {
	return qt(e) && ze(e) == "[object Arguments]";
}, Yt = Object.prototype, Xt = Yt.hasOwnProperty, Zt = Yt.propertyIsEnumerable, Qt = Jt(function() {
	return arguments;
}()) ? Jt : function(e) {
	return qt(e) && Xt.call(e, "callee") && !Zt.call(e, "callee");
}, $t = function() {
	return !1;
}, en = A((function(e, t) {
	var n = t && !t.nodeType && t, r = n && e && !e.nodeType && e, i = r && r.exports === n ? ke.Buffer : void 0;
	e.exports = (i ? i.isBuffer : void 0) || $t;
})), tn = /^(?:0|[1-9]\d*)$/, nn = function(e, t) {
	var n = typeof e;
	return !!(t ??= 9007199254740991) && (n == "number" || n != "symbol" && tn.test(e)) && e > -1 && e % 1 == 0 && e < t;
}, rn = function(e) {
	return typeof e == "number" && e > -1 && e % 1 == 0 && e <= 9007199254740991;
}, R = {};
R["[object Float32Array]"] = R["[object Float64Array]"] = R["[object Int8Array]"] = R["[object Int16Array]"] = R["[object Int32Array]"] = R["[object Uint8Array]"] = R["[object Uint8ClampedArray]"] = R["[object Uint16Array]"] = R["[object Uint32Array]"] = !0, R["[object Arguments]"] = R["[object Array]"] = R["[object ArrayBuffer]"] = R["[object Boolean]"] = R["[object DataView]"] = R["[object Date]"] = R["[object Error]"] = R["[object Function]"] = R["[object Map]"] = R["[object Number]"] = R["[object Object]"] = R["[object RegExp]"] = R["[object Set]"] = R["[object String]"] = R["[object WeakMap]"] = !1;
var an = function(e) {
	return qt(e) && rn(e.length) && !!R[ze(e)];
}, on = function(e) {
	return function(t) {
		return e(t);
	};
}, sn = A((function(e, t) {
	var n = t && !t.nodeType && t, r = n && e && !e.nodeType && e, i = r && r.exports === n && De.process;
	e.exports = function() {
		try {
			return r && r.require && r.require("util").types || i && i.binding && i.binding("util");
		} catch {}
	}();
})), cn = sn && sn.isTypedArray, ln = cn ? on(cn) : an, un = Object.prototype.hasOwnProperty, dn = function(e, t) {
	var n = zt(e), r = !n && Qt(e), i = !n && !r && en(e), a = !n && !r && !i && ln(e), o = n || r || i || a, s = o ? Kt(e.length, String) : [], c = s.length;
	for (var l in e) !t && !un.call(e, l) || o && (l == "length" || i && (l == "offset" || l == "parent") || a && (l == "buffer" || l == "byteLength" || l == "byteOffset") || nn(l, c)) || s.push(l);
	return s;
}, fn = Object.prototype, pn = function(e) {
	var t = e && e.constructor;
	return e === (typeof t == "function" && t.prototype || fn);
}, mn = function(e, t) {
	return function(n) {
		return e(t(n));
	};
}(Object.keys, Object), hn = Object.prototype.hasOwnProperty, gn = function(e) {
	if (!pn(e)) return mn(e);
	var t = [];
	for (var n in Object(e)) hn.call(e, n) && n != "constructor" && t.push(n);
	return t;
}, _n = function(e) {
	return e != null && rn(e.length) && !Ve(e);
}, vn = function(e) {
	return _n(e) ? dn(e) : gn(e);
}, yn = function(e) {
	return Bt(e, vn, Gt);
}, bn = Object.prototype.hasOwnProperty, xn = function(e, t, n, r, i, a) {
	var o = 1 & n, s = yn(e), c = s.length;
	if (c != yn(t).length && !o) return !1;
	for (var l = c; l--;) {
		var u = s[l];
		if (!(o ? u in t : bn.call(t, u))) return !1;
	}
	var d = a.get(e), f = a.get(t);
	if (d && f) return d == t && f == e;
	var p = !0;
	a.set(e, t), a.set(t, e);
	for (var m = o; ++l < c;) {
		var h = e[u = s[l]], g = t[u];
		if (r) var _ = o ? r(g, h, u, t, e, a) : r(h, g, u, e, t, a);
		if (!(_ === void 0 ? h === g || i(h, g, n, r, a) : _)) {
			p = !1;
			break;
		}
		m ||= u == "constructor";
	}
	if (p && !m) {
		var v = e.constructor, y = t.constructor;
		v == y || !("constructor" in e) || !("constructor" in t) || typeof v == "function" && v instanceof v && typeof y == "function" && y instanceof y || (p = !1);
	}
	return a.delete(e), a.delete(t), p;
}, Sn = tt(ke, "DataView"), Cn = tt(ke, "Promise"), wn = tt(ke, "Set"), Tn = tt(ke, "WeakMap"), En = Ke(Sn), Dn = Ke(nt), On = Ke(Cn), kn = Ke(wn), An = Ke(Tn), jn = ze;
(Sn && jn(new Sn(/* @__PURE__ */ new ArrayBuffer(1))) != "[object DataView]" || nt && jn(new nt()) != "[object Map]" || Cn && jn(Cn.resolve()) != "[object Promise]" || wn && jn(new wn()) != "[object Set]" || Tn && jn(new Tn()) != "[object WeakMap]") && (jn = function(e) {
	var t = ze(e), n = t == "[object Object]" ? e.constructor : void 0, r = n ? Ke(n) : "";
	if (r) switch (r) {
		case En: return "[object DataView]";
		case Dn: return "[object Map]";
		case On: return "[object Promise]";
		case kn: return "[object Set]";
		case An: return "[object WeakMap]";
	}
	return t;
});
var Mn = jn, Nn = "[object Object]", Pn = Object.prototype.hasOwnProperty, Fn = function(e, t, n, r, i, a) {
	var o = zt(e), s = zt(t), c = o ? "[object Array]" : Mn(e), l = s ? "[object Array]" : Mn(t), u = (c = c == "[object Arguments]" ? Nn : c) == Nn, d = (l = l == "[object Arguments]" ? Nn : l) == Nn, f = c == l;
	if (f && en(e)) {
		if (!en(t)) return !1;
		o = !0, u = !1;
	}
	if (f && !u) return a ||= new wt(), o || ln(e) ? jt(e, t, n, r, i, a) : Lt(e, t, c, n, r, i, a);
	if (!(1 & n)) {
		var p = u && Pn.call(e, "__wrapped__"), m = d && Pn.call(t, "__wrapped__");
		if (p || m) {
			var h = p ? e.value() : e, g = m ? t.value() : t;
			return a ||= new wt(), i(h, g, n, r, a);
		}
	}
	return !!f && (a ||= new wt(), xn(e, t, n, r, i, a));
}, In = function e(t, n, r, i, a) {
	return t === n || (t == null || n == null || !qt(t) && !qt(n) ? t != t && n != n : Fn(t, n, r, i, e, a));
}, Ln = function(e, t, n, r) {
	var i = n.length, a = i, o = !r;
	if (e == null) return !a;
	for (e = Object(e); i--;) {
		var s = n[i];
		if (o && s[2] ? s[1] !== e[s[0]] : !(s[0] in e)) return !1;
	}
	for (; ++i < a;) {
		var c = (s = n[i])[0], l = e[c], u = s[1];
		if (o && s[2]) {
			if (l === void 0 && !(c in e)) return !1;
		} else {
			var d = new wt();
			if (r) var f = r(l, u, c, e, t, d);
			if (!(f === void 0 ? In(u, l, 3, r, d) : f)) return !1;
		}
	}
	return !0;
}, Rn = function(e) {
	return e == e && !Be(e);
}, zn = function(e) {
	for (var t = vn(e), n = t.length; n--;) {
		var r = t[n], i = e[r];
		t[n] = [
			r,
			i,
			Rn(i)
		];
	}
	return t;
}, Bn = function(e, t) {
	return function(n) {
		return n != null && n[e] === t && (t !== void 0 || e in Object(n));
	};
}, Vn = function(e) {
	var t = zn(e);
	return t.length == 1 && t[0][2] ? Bn(t[0][0], t[0][1]) : function(n) {
		return n === e || Ln(n, e, t);
	};
}, Hn = function(e) {
	return typeof e == "symbol" || qt(e) && ze(e) == "[object Symbol]";
}, Un = /\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/, Wn = /^\w*$/, Gn = function(e, t) {
	if (zt(e)) return !1;
	var n = typeof e;
	return !(n != "number" && n != "symbol" && n != "boolean" && e != null && !Hn(e)) || Wn.test(e) || !Un.test(e) || t != null && e in Object(t);
};
function Kn(e, t) {
	if (typeof e != "function" || t != null && typeof t != "function") throw TypeError("Expected a function");
	var n = function() {
		var r = arguments, i = t ? t.apply(this, r) : r[0], a = n.cache;
		if (a.has(i)) return a.get(i);
		var o = e.apply(this, r);
		return n.cache = a.set(i, o) || a, o;
	};
	return n.cache = new (Kn.Cache || xt)(), n;
}
Kn.Cache = xt;
var qn = Kn, Jn = /[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g, Yn = /\\(\\)?/g, Xn = function(e) {
	var t = qn(e, (function(e) {
		return n.size === 500 && n.clear(), e;
	})), n = t.cache;
	return t;
}((function(e) {
	var t = [];
	return e.charCodeAt(0) === 46 && t.push(""), e.replace(Jn, (function(e, n, r, i) {
		t.push(r ? i.replace(Yn, "$1") : n || e);
	})), t;
})), Zn = function(e, t) {
	for (var n = -1, r = e == null ? 0 : e.length, i = Array(r); ++n < r;) i[n] = t(e[n], n, e);
	return i;
}, Qn = Ae ? Ae.prototype : void 0, $n = Qn ? Qn.toString : void 0, er = function e(t) {
	if (typeof t == "string") return t;
	if (zt(t)) return Zn(t, e) + "";
	if (Hn(t)) return $n ? $n.call(t) : "";
	var n = t + "";
	return n == "0" && 1 / t == -Infinity ? "-0" : n;
}, tr = function(e) {
	return e == null ? "" : er(e);
}, nr = function(e, t) {
	return zt(e) ? e : Gn(e, t) ? [e] : Xn(tr(e));
}, rr = function(e) {
	if (typeof e == "string" || Hn(e)) return e;
	var t = e + "";
	return t == "0" && 1 / e == -Infinity ? "-0" : t;
}, ir = function(e, t) {
	for (var n = 0, r = (t = nr(t, e)).length; e != null && n < r;) e = e[rr(t[n++])];
	return n && n == r ? e : void 0;
}, ar = function(e, t, n) {
	var r = e == null ? void 0 : ir(e, t);
	return r === void 0 ? n : r;
}, or = function(e, t) {
	return e != null && t in Object(e);
}, sr = function(e, t, n) {
	for (var r = -1, i = (t = nr(t, e)).length, a = !1; ++r < i;) {
		var o = rr(t[r]);
		if (!(a = e != null && n(e, o))) break;
		e = e[o];
	}
	return a || ++r != i ? a : !!(i = e == null ? 0 : e.length) && rn(i) && nn(o, i) && (zt(e) || Qt(e));
}, cr = function(e, t) {
	return e != null && sr(e, t, or);
}, lr = function(e, t) {
	return Gn(e) && Rn(t) ? Bn(rr(e), t) : function(n) {
		var r = ar(n, e);
		return r === void 0 && r === t ? cr(n, e) : In(t, r, 3);
	};
}, ur = function(e) {
	return e;
}, dr = function(e) {
	return function(t) {
		return t?.[e];
	};
}, fr = function(e) {
	return function(t) {
		return ir(t, e);
	};
}, pr = function(e) {
	return Gn(e) ? dr(rr(e)) : fr(e);
}, mr = function(e) {
	return typeof e == "function" ? e : e == null ? ur : typeof e == "object" ? zt(e) ? lr(e[0], e[1]) : Vn(e) : pr(e);
}, hr = /\s/, gr = function(e) {
	for (var t = e.length; t-- && hr.test(e.charAt(t)););
	return t;
}, _r = /^\s+/, vr = function(e) {
	return e && e.slice(0, gr(e) + 1).replace(_r, "");
}, yr = /^[-+]0x[0-9a-f]+$/i, br = /^0b[01]+$/i, xr = /^0o[0-7]+$/i, Sr = parseInt, Cr = function(e) {
	if (typeof e == "number") return e;
	if (Hn(e)) return NaN;
	if (Be(e)) {
		var t = typeof e.valueOf == "function" ? e.valueOf() : e;
		e = Be(t) ? t + "" : t;
	}
	if (typeof e != "string") return e === 0 ? e : +e;
	e = vr(e);
	var n = br.test(e);
	return n || xr.test(e) ? Sr(e.slice(2), n ? 2 : 8) : yr.test(e) ? NaN : +e;
}, wr = function(e) {
	return e ? (e = Cr(e)) === Infinity || e === -Infinity ? 17976931348623157e292 * (e < 0 ? -1 : 1) : e == e ? e : 0 : e === 0 ? e : 0;
}, Tr = function(e) {
	var t = wr(e), n = t % 1;
	return t == t ? n ? t - n : t : 0;
};
function Er(e) {
	if (!e) throw Error("change is not provided");
	return ie(e) ? `N${e.oldLineNumber}` : `${P(e) ? "I" : "D"}${e.lineNumber}`;
}
de("old");
var Dr = se("old"), Or = se("new");
ue("old"), ue("new"), de("new"), de("old");
var kr = function() {
	try {
		var e = tt(Object, "defineProperty");
		return e({}, "", {}), e;
	} catch {}
}(), Ar = function(e, t, n) {
	t == "__proto__" && kr ? kr(e, t, {
		configurable: !0,
		enumerable: !0,
		value: n,
		writable: !0
	}) : e[t] = n;
}, jr = function(e) {
	return function(t, n, r) {
		for (var i = -1, a = Object(t), o = r(t), s = o.length; s--;) {
			var c = o[e ? s : ++i];
			if (!1 === n(a[c], c, a)) break;
		}
		return t;
	};
}(), Mr = function(e, t) {
	return e && jr(e, t, vn);
}, Nr = function(e, t) {
	var n = {};
	return t = mr(t), Mr(e, (function(e, r, i) {
		Ar(n, r, t(e, r, i));
	})), n;
}, Pr = [
	"changeKey",
	"text",
	"tokens",
	"renderToken"
], Fr = function e(t, n) {
	var r = t.type, i = t.value, a = t.markType, o = t.properties, s = t.className, c = t.children, l = function(t) {
		return (0, m.jsx)("span", {
			className: t,
			children: i || c && c.map(e)
		}, n);
	};
	switch (r) {
		case "text": return i;
		case "mark": return l(`diff-code-mark diff-code-mark-${a}`);
		case "edit": return l("diff-code-edit");
		default:
			var u = o && o.className;
			return l(j(s || u));
	}
};
function Ir(e) {
	if (!Array.isArray(e)) return !0;
	if (e.length > 1) return !1;
	if (e.length === 1) {
		var t = b(e, 1)[0];
		return t.type === "text" && !t.value;
	}
	return !0;
}
function Lr(e) {
	var t = e.changeKey, n = e.text, r = e.tokens, i = e.renderToken, a = y(e, Pr), o = i ? function(e, t) {
		return i(e, Fr, t);
	} : Fr;
	return (0, m.jsx)("td", _(_({}, a), {}, {
		"data-change-key": t,
		children: r ? Ir(r) ? " " : r.map(o) : n || " "
	}));
}
var Rr = (0, h.memo)(Lr);
function zr(e, t) {
	return function() {
		var n = t === "old" ? Dr(e) : Or(e);
		return n === -1 ? void 0 : n;
	};
}
function Br(e, t) {
	return function(n) {
		return e && n ? (0, m.jsx)("a", {
			href: t ? "#" + t : void 0,
			children: n
		}) : n;
	};
}
function Vr(e, t) {
	return t ? function(n) {
		e(), t(n);
	} : e;
}
function Hr(e, t, n, r) {
	return (0, h.useMemo)((function() {
		var i = Nr(e, (function(e) {
			return function(n) {
				return e && e(t, n);
			};
		}));
		return i.onMouseEnter = Vr(n, i.onMouseEnter), i.onMouseLeave = Vr(r, i.onMouseLeave), i;
	}), [
		e,
		n,
		r,
		t
	]);
}
function Ur(e, t, n, r, i, a, o, s, c) {
	var l = {
		change: t,
		side: r,
		inHoverState: s,
		renderDefault: zr(t, r),
		wrapInAnchor: Br(i, a)
	};
	return (0, m.jsx)("td", _(_({ className: e }, o), {}, {
		"data-change-key": n,
		children: c(l)
	}));
}
function Wr(e) {
	var t, n, r, i = e.change, a = e.selected, o = e.tokens, s = e.className, c = e.generateLineClassName, l = e.gutterClassName, u = e.codeClassName, d = e.gutterEvents, f = e.codeEvents, p = e.hideGutter, g = e.gutterAnchor, v = e.generateAnchorID, y = e.renderToken, x = e.renderGutter, S = i.type, C = i.content, w = Er(i), T = b((t = b((0, h.useState)(!1), 2), n = t[0], r = t[1], [
		n,
		(0, h.useCallback)((function() {
			return r(!0);
		}), []),
		(0, h.useCallback)((function() {
			return r(!1);
		}), [])
	]), 3), E = T[0], D = T[1], O = T[2], k = (0, h.useMemo)((function() {
		return { change: i };
	}), [i]), A = Hr(d, k, D, O), M = Hr(f, k, D, O), ee = v(i), te = c({
		changes: [i],
		defaultGenerate: function() {
			return s;
		}
	}), ne = j("diff-gutter", `diff-gutter-${S}`, l, { "diff-gutter-selected": a }), N = j("diff-code", `diff-code-${S}`, u, { "diff-code-selected": a });
	return (0, m.jsxs)("tr", {
		id: ee,
		className: j("diff-line", te),
		children: [
			!p && Ur(ne, i, w, "old", g, ee, A, E, x),
			!p && Ur(ne, i, w, "new", g, ee, A, E, x),
			(0, m.jsx)(Rr, _({
				className: N,
				changeKey: w,
				text: C,
				tokens: o,
				renderToken: y
			}, M))
		]
	});
}
var Gr = (0, h.memo)(Wr);
function Kr(e) {
	var t = e.hideGutter, n = e.element;
	return (0, m.jsx)("tr", {
		className: "diff-widget",
		children: (0, m.jsx)("td", {
			colSpan: t ? 1 : 3,
			className: "diff-widget-content",
			children: n
		})
	});
}
var qr = [
	"hideGutter",
	"selectedChanges",
	"tokens",
	"lineClassName"
], Jr = [
	"hunk",
	"widgets",
	"className"
];
function Yr(e) {
	var t = e.hunk, n = e.widgets, r = e.className, i = y(e, Jr), a = function(e, t) {
		return e.reduce((function(e, n) {
			var r = Er(n);
			e.push([
				"change",
				r,
				n
			]);
			var i = t[r];
			return i && e.push([
				"widget",
				r,
				i
			]), e;
		}), []);
	}(t.changes, n);
	return (0, m.jsx)("tbody", {
		className: j("diff-hunk", r),
		children: a.map((function(e) {
			return function(e, t) {
				var n = b(e, 3), r = n[0], i = n[1], a = n[2], o = t.hideGutter, s = t.selectedChanges, c = t.tokens, l = t.lineClassName, u = y(t, qr);
				if (r === "change") {
					var d = re(a) ? "old" : "new", f = re(a) ? Dr(a) : Or(a), p = c ? c[d][f - 1] : null;
					return (0, m.jsx)(Gr, _({
						className: l,
						change: a,
						hideGutter: o,
						selected: s.includes(i),
						tokens: p
					}, u), `change${i}`);
				}
				return r === "widget" ? (0, m.jsx)(Kr, {
					hideGutter: o,
					element: a
				}, `widget${i}`) : null;
			}(e, i);
		}))
	});
}
var Xr = 0;
function Zr(e, t, n, r) {
	var i = (0, h.useCallback)((function() {
		return t(e);
	}), [e, t]), a = (0, h.useCallback)((function() {
		return t("");
	}), [t]);
	return (0, h.useMemo)((function() {
		var t = Nr(r, (function(t) {
			return function(r) {
				return t && t({
					side: e,
					change: n
				}, r);
			};
		}));
		return t.onMouseEnter = Vr(i, t.onMouseEnter), t.onMouseLeave = Vr(a, t.onMouseLeave), t;
	}), [
		n,
		r,
		i,
		e,
		a
	]);
}
function Qr(e) {
	var t = e.change, n = e.side, r = e.selected, i = e.tokens, a = e.gutterClassName, o = e.codeClassName, s = e.gutterEvents, c = e.codeEvents, l = e.anchorID, u = e.gutterAnchor, d = e.gutterAnchorTarget, f = e.hideGutter, p = e.hover, h = e.renderToken, g = e.renderGutter;
	if (!t) {
		var y = j("diff-gutter", "diff-gutter-omit", a), b = j("diff-code", "diff-code-omit", o);
		return [!f && (0, m.jsx)("td", { className: y }, "gutter"), (0, m.jsx)("td", { className: b }, "code")];
	}
	var x = t.type, S = t.content, C = Er(t), w = n === Xr ? "old" : "new", T = _({
		id: l || void 0,
		className: j("diff-gutter", `diff-gutter-${x}`, v({ "diff-gutter-selected": r }, "diff-line-hover-" + w, p), a),
		children: g({
			change: t,
			side: w,
			inHoverState: p,
			renderDefault: zr(t, w),
			wrapInAnchor: Br(u, d)
		})
	}, s), E = j("diff-code", `diff-code-${x}`, v({ "diff-code-selected": r }, "diff-line-hover-" + w, p), o);
	return [!f && (0, m.jsx)("td", _(_({}, T), {}, { "data-change-key": C }), "gutter"), (0, m.jsx)(Rr, _({
		className: E,
		changeKey: C,
		text: S,
		tokens: i,
		renderToken: h
	}, c), "code")];
}
function $r(e) {
	var t = e.className, n = e.oldChange, r = e.newChange, i = e.oldSelected, a = e.newSelected, o = e.oldTokens, s = e.newTokens, c = e.monotonous, l = e.gutterClassName, u = e.codeClassName, d = e.gutterEvents, f = e.codeEvents, p = e.hideGutter, g = e.generateAnchorID, v = e.generateLineClassName, y = e.gutterAnchor, x = e.renderToken, S = e.renderGutter, C = b((0, h.useState)(""), 2), w = C[0], T = C[1], E = Zr("old", T, n, d), D = Zr("new", T, r, d), O = Zr("old", T, n, f), k = Zr("new", T, r, f), A = n && g(n), M = r && g(r), ee = v({
		changes: [n, r],
		defaultGenerate: function() {
			return t;
		}
	}), te = {
		monotonous: c,
		hideGutter: p,
		gutterClassName: l,
		codeClassName: u,
		gutterEvents: d,
		codeEvents: f,
		renderToken: x,
		renderGutter: S
	}, ne = _(_({}, te), {}, {
		change: n,
		side: Xr,
		selected: i,
		tokens: o,
		gutterEvents: E,
		codeEvents: O,
		anchorID: A,
		gutterAnchor: y,
		gutterAnchorTarget: A,
		hover: w === "old"
	}), N = _(_({}, te), {}, {
		change: r,
		side: 1,
		selected: a,
		tokens: s,
		gutterEvents: D,
		codeEvents: k,
		anchorID: n === r ? null : M,
		gutterAnchor: y,
		gutterAnchorTarget: n === r ? A : M,
		hover: w === "new"
	});
	return c ? (0, m.jsx)("tr", {
		className: j("diff-line", ee),
		children: Qr(n ? ne : N)
	}) : (0, m.jsxs)("tr", {
		className: j("diff-line", function(e, t) {
			return e && !t ? "diff-line-old-only" : !e && t ? "diff-line-new-only" : e === t ? "diff-line-normal" : "diff-line-compare";
		}(n, r), ee),
		children: [Qr(ne), Qr(N)]
	});
}
var ei = (0, h.memo)($r);
function ti(e) {
	var t = e.hideGutter, n = e.oldElement, r = e.newElement;
	return e.monotonous ? (0, m.jsx)("tr", {
		className: "diff-widget",
		children: (0, m.jsx)("td", {
			colSpan: t ? 1 : 2,
			className: "diff-widget-content",
			children: n || r
		})
	}) : n === r ? (0, m.jsx)("tr", {
		className: "diff-widget",
		children: (0, m.jsx)("td", {
			colSpan: t ? 2 : 4,
			className: "diff-widget-content",
			children: n
		})
	}) : (0, m.jsxs)("tr", {
		className: "diff-widget",
		children: [(0, m.jsx)("td", {
			colSpan: t ? 1 : 2,
			className: "diff-widget-content",
			children: n
		}), (0, m.jsx)("td", {
			colSpan: t ? 1 : 2,
			className: "diff-widget-content",
			children: r
		})]
	});
}
var ni = [
	"selectedChanges",
	"monotonous",
	"hideGutter",
	"tokens",
	"lineClassName"
], ri = [
	"hunk",
	"widgets",
	"className"
];
function ii(e, t) {
	return (e ? Er(e) : "00") + (t ? Er(t) : "00");
}
function ai(e) {
	var t = e.hunk, n = e.widgets, r = e.className, i = y(e, ri), a = function(e, t) {
		for (var n = function(e) {
			return e && t[Er(e)] || null;
		}, r = [], i = 0; i < e.length; i++) {
			var a = e[i];
			if (ie(a)) r.push([
				"change",
				ii(a, a),
				a,
				a
			]);
			else if (re(a)) {
				var o = e[i + 1];
				o && P(o) ? (i += 1, r.push([
					"change",
					ii(a, o),
					a,
					o
				])) : r.push([
					"change",
					ii(a, null),
					a,
					null
				]);
			} else r.push([
				"change",
				ii(null, a),
				null,
				a
			]);
			var s = r[r.length - 1], c = n(s[2]), l = n(s[3]);
			if (c || l) {
				var u = s[1];
				r.push([
					"widget",
					u,
					c,
					l
				]);
			}
		}
		return r;
	}(t.changes, n);
	return (0, m.jsx)("tbody", {
		className: j("diff-hunk", r),
		children: a.map((function(e) {
			return function(e, t) {
				var n = b(e, 4), r = n[0], i = n[1], a = n[2], o = n[3], s = t.selectedChanges, c = t.monotonous, l = t.hideGutter, u = t.tokens, d = t.lineClassName, f = y(t, ni);
				if (r === "change") {
					var p = !!a && s.includes(Er(a)), h = !!o && s.includes(Er(o)), g = a && u ? u.old[Dr(a) - 1] : null, v = o && u ? u.new[Or(o) - 1] : null;
					return (0, m.jsx)(ei, _({
						className: d,
						oldChange: a,
						newChange: o,
						monotonous: c,
						hideGutter: l,
						oldSelected: p,
						newSelected: h,
						oldTokens: g,
						newTokens: v
					}, f), `change${i}`);
				}
				return r === "widget" ? (0, m.jsx)(ti, {
					monotonous: c,
					hideGutter: l,
					oldElement: a,
					newElement: o
				}, `widget${i}`) : null;
			}(e, i);
		}))
	});
}
var oi = ["gutterType", "hunkClassName"];
function si(e) {
	var t = e.hunk, n = ne(), r = n.gutterType, i = n.hunkClassName, a = y(n, oi), o = r === "none", s = r === "anchor", c = a.viewType === "unified" ? Yr : ai;
	return (0, m.jsx)(c, _(_({}, a), {}, {
		hunk: t,
		hideGutter: o,
		gutterAnchor: s,
		className: i
	}));
}
function ci() {}
function li(e, t) {
	var n = t ? "auto" : "none";
	e instanceof HTMLElement && e.style.userSelect !== n && (e.style.userSelect = n);
}
function ui(e) {
	return e.map((function(e) {
		return (0, m.jsx)(si, { hunk: e }, function(e) {
			return `-${e.oldStart},${e.oldLines} +${e.newStart},${e.newLines}`;
		}(e));
	}));
}
function di(e) {
	var t = e.diffType, n = e.hunks, r = e.optimizeSelection, i = e.className, a = e.hunkClassName, o = a === void 0 ? M.hunkClassName : a, s = e.lineClassName, c = s === void 0 ? M.lineClassName : s, l = e.generateLineClassName, u = l === void 0 ? M.generateLineClassName : l, d = e.gutterClassName, f = d === void 0 ? M.gutterClassName : d, p = e.codeClassName, g = p === void 0 ? M.codeClassName : p, _ = e.gutterType, v = _ === void 0 ? M.gutterType : _, y = e.viewType, b = y === void 0 ? M.viewType : y, x = e.gutterEvents, C = x === void 0 ? M.gutterEvents : x, w = e.codeEvents, T = w === void 0 ? M.codeEvents : w, E = e.generateAnchorID, D = E === void 0 ? M.generateAnchorID : E, k = e.selectedChanges, A = k === void 0 ? M.selectedChanges : k, ee = e.widgets, ne = ee === void 0 ? M.widgets : ee, N = e.renderGutter, P = N === void 0 ? M.renderGutter : N, re = e.tokens, ie = e.renderToken, ae = e.children, oe = ae === void 0 ? ui : ae, F = (0, h.useRef)(null), I = (0, h.useCallback)((function(e) {
		var t = e.target;
		if (e.button === 0) {
			var n = function(e, t) {
				for (var n = e; n && n !== document.documentElement && !n.classList.contains(t);) n = n.parentElement;
				return n === document.documentElement ? null : n;
			}(t, "diff-code");
			if (n && n.parentElement) {
				var r = window.getSelection();
				r && r.removeAllRanges();
				var i = S(n.parentElement.children).indexOf(n);
				if (i === 1 || i === 3) {
					var a, o = O(F.current ? F.current.querySelectorAll(".diff-line") : []);
					try {
						for (o.s(); !(a = o.n()).done;) {
							var s = a.value.children;
							li(s[1], i === 1), li(s[3], i === 3);
						}
					} catch (e) {
						o.e(e);
					} finally {
						o.f();
					}
				}
			}
		}
	}), []), L = v === "none", se = t === "add" || t === "delete", ce = b === "split" && !se && r ? I : ci, le = (0, h.useMemo)((function() {
		return (0, m.jsxs)("colgroup", b === "unified" ? { children: [
			!L && (0, m.jsx)("col", { className: "diff-gutter-col" }),
			!L && (0, m.jsx)("col", { className: "diff-gutter-col" }),
			(0, m.jsx)("col", {})
		] } : se ? { children: [!L && (0, m.jsx)("col", { className: "diff-gutter-col" }), (0, m.jsx)("col", {})] } : { children: [
			!L && (0, m.jsx)("col", { className: "diff-gutter-col" }),
			(0, m.jsx)("col", {}),
			!L && (0, m.jsx)("col", { className: "diff-gutter-col" }),
			(0, m.jsx)("col", {})
		] });
	}), [
		b,
		se,
		L
	]), ue = (0, h.useMemo)((function() {
		return {
			hunkClassName: o,
			lineClassName: c,
			generateLineClassName: u,
			gutterClassName: f,
			codeClassName: g,
			monotonous: se,
			hideGutter: L,
			viewType: b,
			gutterType: v,
			codeEvents: T,
			gutterEvents: C,
			generateAnchorID: D,
			selectedChanges: A,
			widgets: ne,
			renderGutter: P,
			tokens: re,
			renderToken: ie
		};
	}), [
		g,
		T,
		D,
		f,
		C,
		v,
		L,
		o,
		c,
		u,
		se,
		P,
		ie,
		A,
		re,
		b,
		ne
	]);
	return (0, m.jsx)(te, {
		value: ue,
		children: (0, m.jsxs)("table", {
			ref: F,
			className: j("diff", `diff-${b}`, i),
			onMouseDown: ce,
			children: [le, oe(n)]
		})
	});
}
var fi = (0, h.memo)(di), pi = function(e, t, n, r) {
	for (var i = -1, a = e == null ? 0 : e.length; ++i < a;) {
		var o = e[i];
		t(r, o, n(o), e);
	}
	return r;
}, mi = function(e, t) {
	return function(n, r) {
		if (n == null) return n;
		if (!_n(n)) return e(n, r);
		for (var i = n.length, a = t ? i : -1, o = Object(n); (t ? a-- : ++a < i) && !1 !== r(o[a], a, o););
		return n;
	};
}(Mr), hi = function(e, t, n, r) {
	return mi(e, (function(e, i, a) {
		t(r, e, n(e), a);
	})), r;
}, gi = function(e, t) {
	return function(n, r) {
		var i = zt(n) ? pi : hi, a = t ? t() : {};
		return i(n, e, mr(r), a);
	};
}, _i = gi((function(e, t, n) {
	Ar(e, n, t);
})), vi = Ae ? Ae.isConcatSpreadable : void 0, yi = function(e) {
	return zt(e) || Qt(e) || !!(vi && e && e[vi]);
}, bi = function e(t, n, r, i, a) {
	var o = -1, s = t.length;
	for (r ||= yi, a ||= []; ++o < s;) {
		var c = t[o];
		n > 0 && r(c) ? n > 1 ? e(c, n - 1, r, i, a) : Rt(a, c) : i || (a[a.length] = c);
	}
	return a;
}, xi = function(e, t) {
	var n = -1, r = _n(e) ? Array(e.length) : [];
	return mi(e, (function(e, i, a) {
		r[++n] = t(e, i, a);
	})), r;
}, Si = function(e, t) {
	return (zt(e) ? Zn : xi)(e, mr(t));
}, Ci = function(e, t) {
	return bi(Si(e, t), 1);
};
function wi(e, t) {
	var n = t.newStart;
	return b(t.changes.reduce((function(e, t) {
		var n = b(e, 2), r = n[0], i = n[1];
		return re(t) ? (r.splice(i, 1), [r, i]) : (P(t) && r.splice(i, 0, t.content), [r, i + 1]);
	}), [e, n - 1]), 1)[0];
}
function Ti(e, t, n) {
	if (!e.length) return [];
	var r = t === "old" ? Dr : Or, i = _i(e, r), a = r(e[e.length - 1]);
	return Array.from({ length: a }).map((function(e, t) {
		return n(i[t + 1]);
	}));
}
function Ei(e) {
	var t = b(function(e) {
		return Ci(e, (function(e) {
			return e.changes;
		})).reduce((function(e, t) {
			var n = b(e, 2), r = n[0], i = n[1];
			return ie(t) ? (r.push(t), i.push(t)) : re(t) ? r.push(t) : i.push(t), [r, i];
		}), [[], []]);
	}(e), 2), n = t[0], r = t[1], i = function(e) {
		return e ? e.content : "";
	};
	return [Ti(n, "old", i).join("\n"), Ti(r, "new", i).join("\n")];
}
function Di(e) {
	return {
		type: "root",
		children: e
	};
}
function Oi(e, t) {
	if (t.oldSource) {
		var n = function(e, t) {
			return t.reduce(wi, e.split("\n")).join("\n");
		}(t.oldSource, e), r = t.highlight ? function(e) {
			return t.refractor.highlight(e, t.language);
		} : function(e) {
			return [{
				type: "text",
				value: e
			}];
		};
		return [Di(r(t.oldSource)), Di(r(n))];
	}
	var i = b(Ei(e), 2), a = i[0], o = i[1], s = t.highlight ? function(e) {
		return Di(t.refractor.highlight(e, t.language));
	} : function(e) {
		return Di([{
			type: "text",
			value: e
		}]);
	};
	return [s(a), s(o)];
}
function ki(e) {
	return e.map((function(e) {
		return _({}, e);
	}));
}
function Ai(e, t) {
	return [].concat(S(ki(e.slice(0, -1))), [t]);
}
function ji(e) {
	return e.type === "text";
}
function Mi(e) {
	var t = e[e.length - 1];
	if (ji(t)) return t;
	throw Error(`Invalid token path with leaf of type ${t.type}`);
}
function Ni(e, t, n, r) {
	var i = e.slice(0, -1), a = Mi(e), o = [];
	if (n <= 0 || t >= a?.value.length) return [e];
	var s = function(e, t) {
		var n = a.value.slice(e, t);
		return [].concat(S(i), [_(_({}, a), {}, { value: n })]);
	};
	if (t > 0) {
		var c = s(0, t);
		o.push(ki(c));
	}
	var l = s(Math.max(t, 0), n);
	if (o.push(r ? function(e, t) {
		return [t].concat(S(ki(e)));
	}(l, r) : ki(l)), n < a.value.length) {
		var u = s(n);
		o.push(ki(u));
	}
	return o;
}
var z = ["children"];
function B(e) {
	var t = arguments.length > 1 && arguments[1] !== void 0 ? arguments[1] : [], n = arguments.length > 2 && arguments[2] !== void 0 ? arguments[2] : [];
	if (e.children) {
		var r = e.children, i = y(e, z);
		n.push(i);
		var a, o = O(r);
		try {
			for (o.s(); !(a = o.n()).done;) B(a.value, t, n);
		} catch (e) {
			o.e(e);
		} finally {
			o.f();
		}
		n.pop();
	} else t.push(ki([].concat(S(n.slice(1)), [e])));
	return t;
}
function Pi(e) {
	return e.reduce((function(e, t) {
		var n = e[e.length - 1], r = x(function(e) {
			var t = Mi(e);
			return t.value.includes("\n") ? t.value.split("\n").map((function(n) {
				return Ai(e, _(_({}, t), {}, { value: n }));
			})) : [e];
		}(t)), i = r[0], a = r.slice(1);
		return [].concat(S(e.slice(0, -1)), [[].concat(S(n), [i])], S(a.map((function(e) {
			return [e];
		}))));
	}), [[]]);
}
function Fi(e) {
	return Pi(B(e));
}
var Ii = function(e, t, n) {
	var r = (n = typeof n == "function" ? n : void 0) ? n(e, t) : void 0;
	return r === void 0 ? In(e, t, void 0, n) : !!r;
}, Li = function(e, t) {
	return In(e, t);
}, Ri = function(e) {
	var t = e == null ? 0 : e.length;
	return t ? e[t - 1] : void 0;
};
function zi(e, t) {
	if (!e.children) throw Error("parent node missing children property");
	var n, r, i = Ri(e.children);
	return i && (r = t, (n = i).type === r.type && (n.type === "text" || n.children && r.children && Ii(n, r, (function(e, t, n) {
		return n === "chlidren" || Li(e, t);
	})))) ? e.children[e.children.length - 1] = function(e, t) {
		return "value" in e && "value" in t ? _(_({}, e), {}, { value: `${e.value}${t.value}` }) : e;
	}(i, t) : e.children.push(t), e.children[e.children.length - 1];
}
function Bi(e) {
	var t, n = {
		type: "root",
		children: []
	}, r = O(e);
	try {
		var i = function() {
			var e = t.value;
			e.reduce((function(t, n, r) {
				return zi(t, r === e.length - 1 ? _({}, n) : _(_({}, n), {}, { children: [] }));
			}), n);
		};
		for (r.s(); !(t = r.n()).done;) i();
	} catch (e) {
		r.e(e);
	} finally {
		r.f();
	}
	return n;
}
var Vi = Object.prototype.hasOwnProperty, Hi = gi((function(e, t, n) {
	Vi.call(e, n) ? e[n].push(t) : Ar(e, n, [t]);
})), Ui = Object.prototype.hasOwnProperty, Wi = function(e) {
	if (e == null) return !0;
	if (_n(e) && (zt(e) || typeof e == "string" || typeof e.splice == "function" || en(e) || ln(e) || Qt(e))) return !e.length;
	var t = Mn(e);
	if (t == "[object Map]" || t == "[object Set]") return !e.size;
	if (pn(e)) return !gn(e).length;
	for (var n in e) if (Ui.call(e, n)) return !1;
	return !0;
}, Gi = function(e, t) {
	var n = t.start, r = n + t.length;
	return b(e.reduce((function(e, i) {
		var a = b(e, 2), o = a[0], s = a[1], c = s + Mi(i).value.length;
		if (s > r || c < n) o.push(i);
		else {
			var l = Ni(i, n - s, r - s, t);
			o.push.apply(o, S(l));
		}
		return [o, c];
	}), [[], 0]), 1)[0];
};
function Ki(e, t) {
	var n = Hi(t, "lineNumber");
	return e.map((function(e, t) {
		return function(e, t) {
			return Wi(t) ? e : t.reduce(Gi, e);
		}(e, n[t + 1]);
	}));
}
function qi(e, t) {
	return function(n) {
		var r = b(n, 2), i = r[0], a = r[1];
		return [Ki(i, e), Ki(a, t)];
	};
}
var Ji = function(e) {
	return e != null && e.length ? bi(e, 1) : [];
}, Yi = Math.max, Xi = function(e, t, n) {
	var r = e == null ? 0 : e.length;
	if (!r) return -1;
	var i = n == null ? 0 : Tr(n);
	return i < 0 && (i = Yi(r + i, 0)), fe(e, mr(t), i);
}, Zi = A((function(e) {
	var t = function() {
		this.Diff_Timeout = 1, this.Diff_EditCost = 4, this.Match_Threshold = .5, this.Match_Distance = 1e3, this.Patch_DeleteThreshold = .5, this.Patch_Margin = 4, this.Match_MaxBits = 32;
	};
	t.Diff = function(e, t) {
		return [e, t];
	}, t.prototype.diff_main = function(e, n, r, i) {
		i === void 0 && (i = this.Diff_Timeout <= 0 ? Number.MAX_VALUE : (/* @__PURE__ */ new Date()).getTime() + 1e3 * this.Diff_Timeout);
		var a = i;
		if (e == null || n == null) throw Error("Null input. (diff_main)");
		if (e == n) return e ? [new t.Diff(0, e)] : [];
		r === void 0 && (r = !0);
		var o = r, s = this.diff_commonPrefix(e, n), c = e.substring(0, s);
		e = e.substring(s), n = n.substring(s), s = this.diff_commonSuffix(e, n);
		var l = e.substring(e.length - s);
		e = e.substring(0, e.length - s), n = n.substring(0, n.length - s);
		var u = this.diff_compute_(e, n, o, a);
		return c && u.unshift(new t.Diff(0, c)), l && u.push(new t.Diff(0, l)), this.diff_cleanupMerge(u), u;
	}, t.prototype.diff_compute_ = function(e, n, r, i) {
		var a;
		if (!e) return [new t.Diff(1, n)];
		if (!n) return [new t.Diff(-1, e)];
		var o = e.length > n.length ? e : n, s = e.length > n.length ? n : e, c = o.indexOf(s);
		if (c != -1) return a = [
			new t.Diff(1, o.substring(0, c)),
			new t.Diff(0, s),
			new t.Diff(1, o.substring(c + s.length))
		], e.length > n.length && (a[0][0] = a[2][0] = -1), a;
		if (s.length == 1) return [new t.Diff(-1, e), new t.Diff(1, n)];
		var l = this.diff_halfMatch_(e, n);
		if (l) {
			var u = l[0], d = l[1], f = l[2], p = l[3], m = l[4], h = this.diff_main(u, f, r, i), g = this.diff_main(d, p, r, i);
			return h.concat([new t.Diff(0, m)], g);
		}
		return r && e.length > 100 && n.length > 100 ? this.diff_lineMode_(e, n, i) : this.diff_bisect_(e, n, i);
	}, t.prototype.diff_lineMode_ = function(e, n, r) {
		var i = this.diff_linesToChars_(e, n);
		e = i.chars1, n = i.chars2;
		var a = i.lineArray, o = this.diff_main(e, n, !1, r);
		this.diff_charsToLines_(o, a), this.diff_cleanupSemantic(o), o.push(new t.Diff(0, ""));
		for (var s = 0, c = 0, l = 0, u = "", d = ""; s < o.length;) {
			switch (o[s][0]) {
				case 1:
					l++, d += o[s][1];
					break;
				case -1:
					c++, u += o[s][1];
					break;
				case 0:
					if (c >= 1 && l >= 1) {
						o.splice(s - c - l, c + l), s = s - c - l;
						for (var f = this.diff_main(u, d, !1, r), p = f.length - 1; p >= 0; p--) o.splice(s, 0, f[p]);
						s += f.length;
					}
					l = 0, c = 0, u = "", d = "";
			}
			s++;
		}
		return o.pop(), o;
	}, t.prototype.diff_bisect_ = function(e, n, r) {
		for (var i = e.length, a = n.length, o = Math.ceil((i + a) / 2), s = o, c = 2 * o, l = Array(c), u = Array(c), d = 0; d < c; d++) l[d] = -1, u[d] = -1;
		l[s + 1] = 0, u[s + 1] = 0;
		for (var f = i - a, p = f % 2 != 0, m = 0, h = 0, g = 0, _ = 0, v = 0; v < o && !((/* @__PURE__ */ new Date()).getTime() > r); v++) {
			for (var y = -v + m; y <= v - h; y += 2) {
				for (var b = s + y, x = (E = y == -v || y != v && l[b - 1] < l[b + 1] ? l[b + 1] : l[b - 1] + 1) - y; E < i && x < a && e.charAt(E) == n.charAt(x);) E++, x++;
				if (l[b] = E, E > i) h += 2;
				else if (x > a) m += 2;
				else if (p && (w = s + f - y) >= 0 && w < c && u[w] != -1 && E >= (C = i - u[w])) return this.diff_bisectSplit_(e, n, E, x, r);
			}
			for (var S = -v + g; S <= v - _; S += 2) {
				for (var C, w = s + S, T = (C = S == -v || S != v && u[w - 1] < u[w + 1] ? u[w + 1] : u[w - 1] + 1) - S; C < i && T < a && e.charAt(i - C - 1) == n.charAt(a - T - 1);) C++, T++;
				if (u[w] = C, C > i) _ += 2;
				else if (T > a) g += 2;
				else if (!p && (b = s + f - S) >= 0 && b < c && l[b] != -1) {
					var E;
					if (x = s + (E = l[b]) - b, E >= (C = i - C)) return this.diff_bisectSplit_(e, n, E, x, r);
				}
			}
		}
		return [new t.Diff(-1, e), new t.Diff(1, n)];
	}, t.prototype.diff_bisectSplit_ = function(e, t, n, r, i) {
		var a = e.substring(0, n), o = t.substring(0, r), s = e.substring(n), c = t.substring(r), l = this.diff_main(a, o, !1, i), u = this.diff_main(s, c, !1, i);
		return l.concat(u);
	}, t.prototype.diff_linesToChars_ = function(e, t) {
		var n = [], r = {};
		function i(e) {
			for (var t = "", i = 0, o = -1, s = n.length; o < e.length - 1;) {
				(o = e.indexOf("\n", i)) == -1 && (o = e.length - 1);
				var c = e.substring(i, o + 1);
				(r.hasOwnProperty ? r.hasOwnProperty(c) : r[c] !== void 0) ? t += String.fromCharCode(r[c]) : (s == a && (c = e.substring(i), o = e.length), t += String.fromCharCode(s), r[c] = s, n[s++] = c), i = o + 1;
			}
			return t;
		}
		n[0] = "";
		var a = 4e4, o = i(e);
		return a = 65535, {
			chars1: o,
			chars2: i(t),
			lineArray: n
		};
	}, t.prototype.diff_charsToLines_ = function(e, t) {
		for (var n = 0; n < e.length; n++) {
			for (var r = e[n][1], i = [], a = 0; a < r.length; a++) i[a] = t[r.charCodeAt(a)];
			e[n][1] = i.join("");
		}
	}, t.prototype.diff_commonPrefix = function(e, t) {
		if (!e || !t || e.charAt(0) != t.charAt(0)) return 0;
		for (var n = 0, r = Math.min(e.length, t.length), i = r, a = 0; n < i;) e.substring(a, i) == t.substring(a, i) ? a = n = i : r = i, i = Math.floor((r - n) / 2 + n);
		return i;
	}, t.prototype.diff_commonSuffix = function(e, t) {
		if (!e || !t || e.charAt(e.length - 1) != t.charAt(t.length - 1)) return 0;
		for (var n = 0, r = Math.min(e.length, t.length), i = r, a = 0; n < i;) e.substring(e.length - i, e.length - a) == t.substring(t.length - i, t.length - a) ? a = n = i : r = i, i = Math.floor((r - n) / 2 + n);
		return i;
	}, t.prototype.diff_commonOverlap_ = function(e, t) {
		var n = e.length, r = t.length;
		if (n == 0 || r == 0) return 0;
		n > r ? e = e.substring(n - r) : n < r && (t = t.substring(0, n));
		var i = Math.min(n, r);
		if (e == t) return i;
		for (var a = 0, o = 1;;) {
			var s = e.substring(i - o), c = t.indexOf(s);
			if (c == -1) return a;
			o += c, c != 0 && e.substring(i - o) != t.substring(0, o) || (a = o, o++);
		}
	}, t.prototype.diff_halfMatch_ = function(e, t) {
		if (this.Diff_Timeout <= 0) return null;
		var n = e.length > t.length ? e : t, r = e.length > t.length ? t : e;
		if (n.length < 4 || 2 * r.length < n.length) return null;
		var i = this;
		function a(e, t, n) {
			for (var r, a, o, s, c = e.substring(n, n + Math.floor(e.length / 4)), l = -1, u = ""; (l = t.indexOf(c, l + 1)) != -1;) {
				var d = i.diff_commonPrefix(e.substring(n), t.substring(l)), f = i.diff_commonSuffix(e.substring(0, n), t.substring(0, l));
				u.length < f + d && (u = t.substring(l - f, l) + t.substring(l, l + d), r = e.substring(0, n - f), a = e.substring(n + d), o = t.substring(0, l - f), s = t.substring(l + d));
			}
			return 2 * u.length >= e.length ? [
				r,
				a,
				o,
				s,
				u
			] : null;
		}
		var o, s, c, l, u, d = a(n, r, Math.ceil(n.length / 4)), f = a(n, r, Math.ceil(n.length / 2));
		return d || f ? (o = f ? d && d[4].length > f[4].length ? d : f : d, e.length > t.length ? (s = o[0], c = o[1], l = o[2], u = o[3]) : (l = o[0], u = o[1], s = o[2], c = o[3]), [
			s,
			c,
			l,
			u,
			o[4]
		]) : null;
	}, t.prototype.diff_cleanupSemantic = function(e) {
		for (var n = !1, r = [], i = 0, a = null, o = 0, s = 0, c = 0, l = 0, u = 0; o < e.length;) e[o][0] == 0 ? (r[i++] = o, s = l, c = u, l = 0, u = 0, a = e[o][1]) : (e[o][0] == 1 ? l += e[o][1].length : u += e[o][1].length, a && a.length <= Math.max(s, c) && a.length <= Math.max(l, u) && (e.splice(r[i - 1], 0, new t.Diff(-1, a)), e[r[i - 1] + 1][0] = 1, i--, o = --i > 0 ? r[i - 1] : -1, s = 0, c = 0, l = 0, u = 0, a = null, n = !0)), o++;
		for (n && this.diff_cleanupMerge(e), this.diff_cleanupSemanticLossless(e), o = 1; o < e.length;) {
			if (e[o - 1][0] == -1 && e[o][0] == 1) {
				var d = e[o - 1][1], f = e[o][1], p = this.diff_commonOverlap_(d, f), m = this.diff_commonOverlap_(f, d);
				p >= m ? (p >= d.length / 2 || p >= f.length / 2) && (e.splice(o, 0, new t.Diff(0, f.substring(0, p))), e[o - 1][1] = d.substring(0, d.length - p), e[o + 1][1] = f.substring(p), o++) : (m >= d.length / 2 || m >= f.length / 2) && (e.splice(o, 0, new t.Diff(0, d.substring(0, m))), e[o - 1][0] = 1, e[o - 1][1] = f.substring(0, f.length - m), e[o + 1][0] = -1, e[o + 1][1] = d.substring(m), o++), o++;
			}
			o++;
		}
	}, t.prototype.diff_cleanupSemanticLossless = function(e) {
		function n(e, n) {
			if (!e || !n) return 6;
			var r = e.charAt(e.length - 1), i = n.charAt(0), a = r.match(t.nonAlphaNumericRegex_), o = i.match(t.nonAlphaNumericRegex_), s = a && r.match(t.whitespaceRegex_), c = o && i.match(t.whitespaceRegex_), l = s && r.match(t.linebreakRegex_), u = c && i.match(t.linebreakRegex_), d = l && e.match(t.blanklineEndRegex_), f = u && n.match(t.blanklineStartRegex_);
			return d || f ? 5 : l || u ? 4 : a && !s && c ? 3 : s || c ? 2 : a || o ? 1 : 0;
		}
		for (var r = 1; r < e.length - 1;) {
			if (e[r - 1][0] == 0 && e[r + 1][0] == 0) {
				var i = e[r - 1][1], a = e[r][1], o = e[r + 1][1], s = this.diff_commonSuffix(i, a);
				if (s) {
					var c = a.substring(a.length - s);
					i = i.substring(0, i.length - s), a = c + a.substring(0, a.length - s), o = c + o;
				}
				for (var l = i, u = a, d = o, f = n(i, a) + n(a, o); a.charAt(0) === o.charAt(0);) {
					i += a.charAt(0), a = a.substring(1) + o.charAt(0), o = o.substring(1);
					var p = n(i, a) + n(a, o);
					p >= f && (f = p, l = i, u = a, d = o);
				}
				e[r - 1][1] != l && (l ? e[r - 1][1] = l : (e.splice(r - 1, 1), r--), e[r][1] = u, d ? e[r + 1][1] = d : (e.splice(r + 1, 1), r--));
			}
			r++;
		}
	}, t.nonAlphaNumericRegex_ = /[^a-zA-Z0-9]/, t.whitespaceRegex_ = /\s/, t.linebreakRegex_ = /[\r\n]/, t.blanklineEndRegex_ = /\n\r?\n$/, t.blanklineStartRegex_ = /^\r?\n\r?\n/, t.prototype.diff_cleanupEfficiency = function(e) {
		for (var n = !1, r = [], i = 0, a = null, o = 0, s = !1, c = !1, l = !1, u = !1; o < e.length;) e[o][0] == 0 ? (e[o][1].length < this.Diff_EditCost && (l || u) ? (r[i++] = o, s = l, c = u, a = e[o][1]) : (i = 0, a = null), l = u = !1) : (e[o][0] == -1 ? u = !0 : l = !0, a && (s && c && l && u || a.length < this.Diff_EditCost / 2 && s + c + l + u == 3) && (e.splice(r[i - 1], 0, new t.Diff(-1, a)), e[r[i - 1] + 1][0] = 1, i--, a = null, s && c ? (l = u = !0, i = 0) : (o = --i > 0 ? r[i - 1] : -1, l = u = !1), n = !0)), o++;
		n && this.diff_cleanupMerge(e);
	}, t.prototype.diff_cleanupMerge = function(e) {
		e.push(new t.Diff(0, ""));
		for (var n, r = 0, i = 0, a = 0, o = "", s = ""; r < e.length;) switch (e[r][0]) {
			case 1:
				a++, s += e[r][1], r++;
				break;
			case -1:
				i++, o += e[r][1], r++;
				break;
			case 0: i + a > 1 ? (i !== 0 && a !== 0 && ((n = this.diff_commonPrefix(s, o)) !== 0 && (r - i - a > 0 && e[r - i - a - 1][0] == 0 ? e[r - i - a - 1][1] += s.substring(0, n) : (e.splice(0, 0, new t.Diff(0, s.substring(0, n))), r++), s = s.substring(n), o = o.substring(n)), (n = this.diff_commonSuffix(s, o)) !== 0 && (e[r][1] = s.substring(s.length - n) + e[r][1], s = s.substring(0, s.length - n), o = o.substring(0, o.length - n))), r -= i + a, e.splice(r, i + a), o.length && (e.splice(r, 0, new t.Diff(-1, o)), r++), s.length && (e.splice(r, 0, new t.Diff(1, s)), r++), r++) : r !== 0 && e[r - 1][0] == 0 ? (e[r - 1][1] += e[r][1], e.splice(r, 1)) : r++, a = 0, i = 0, o = "", s = "";
		}
		e[e.length - 1][1] === "" && e.pop();
		var c = !1;
		for (r = 1; r < e.length - 1;) e[r - 1][0] == 0 && e[r + 1][0] == 0 && (e[r][1].substring(e[r][1].length - e[r - 1][1].length) == e[r - 1][1] ? (e[r][1] = e[r - 1][1] + e[r][1].substring(0, e[r][1].length - e[r - 1][1].length), e[r + 1][1] = e[r - 1][1] + e[r + 1][1], e.splice(r - 1, 1), c = !0) : e[r][1].substring(0, e[r + 1][1].length) == e[r + 1][1] && (e[r - 1][1] += e[r + 1][1], e[r][1] = e[r][1].substring(e[r + 1][1].length) + e[r + 1][1], e.splice(r + 1, 1), c = !0)), r++;
		c && this.diff_cleanupMerge(e);
	}, t.prototype.diff_xIndex = function(e, t) {
		var n, r = 0, i = 0, a = 0, o = 0;
		for (n = 0; n < e.length && (e[n][0] !== 1 && (r += e[n][1].length), e[n][0] !== -1 && (i += e[n][1].length), !(r > t)); n++) a = r, o = i;
		return e.length != n && e[n][0] === -1 ? o : o + (t - a);
	}, t.prototype.diff_prettyHtml = function(e) {
		for (var t = [], n = /&/g, r = /</g, i = />/g, a = /\n/g, o = 0; o < e.length; o++) {
			var s = e[o][0], c = e[o][1].replace(n, "&amp;").replace(r, "&lt;").replace(i, "&gt;").replace(a, "&para;<br>");
			switch (s) {
				case 1:
					t[o] = "<ins style=\"background:#e6ffe6;\">" + c + "</ins>";
					break;
				case -1:
					t[o] = "<del style=\"background:#ffe6e6;\">" + c + "</del>";
					break;
				case 0: t[o] = "<span>" + c + "</span>";
			}
		}
		return t.join("");
	}, t.prototype.diff_text1 = function(e) {
		for (var t = [], n = 0; n < e.length; n++) e[n][0] !== 1 && (t[n] = e[n][1]);
		return t.join("");
	}, t.prototype.diff_text2 = function(e) {
		for (var t = [], n = 0; n < e.length; n++) e[n][0] !== -1 && (t[n] = e[n][1]);
		return t.join("");
	}, t.prototype.diff_levenshtein = function(e) {
		for (var t = 0, n = 0, r = 0, i = 0; i < e.length; i++) {
			var a = e[i][0], o = e[i][1];
			switch (a) {
				case 1:
					n += o.length;
					break;
				case -1:
					r += o.length;
					break;
				case 0: t += Math.max(n, r), n = 0, r = 0;
			}
		}
		return t += Math.max(n, r);
	}, t.prototype.diff_toDelta = function(e) {
		for (var t = [], n = 0; n < e.length; n++) switch (e[n][0]) {
			case 1:
				t[n] = "+" + encodeURI(e[n][1]);
				break;
			case -1:
				t[n] = "-" + e[n][1].length;
				break;
			case 0: t[n] = "=" + e[n][1].length;
		}
		return t.join("	").replace(/%20/g, " ");
	}, t.prototype.diff_fromDelta = function(e, n) {
		for (var r = [], i = 0, a = 0, o = n.split(/\t/g), s = 0; s < o.length; s++) {
			var c = o[s].substring(1);
			switch (o[s].charAt(0)) {
				case "+":
					try {
						r[i++] = new t.Diff(1, decodeURI(c));
					} catch {
						throw Error("Illegal escape in diff_fromDelta: " + c);
					}
					break;
				case "-":
				case "=":
					var l = parseInt(c, 10);
					if (isNaN(l) || l < 0) throw Error("Invalid number in diff_fromDelta: " + c);
					var u = e.substring(a, a += l);
					o[s].charAt(0) == "=" ? r[i++] = new t.Diff(0, u) : r[i++] = new t.Diff(-1, u);
					break;
				default: if (o[s]) throw Error("Invalid diff operation in diff_fromDelta: " + o[s]);
			}
		}
		if (a != e.length) throw Error("Delta length (" + a + ") does not equal source text length (" + e.length + ").");
		return r;
	}, t.prototype.match_main = function(e, t, n) {
		if (e == null || t == null || n == null) throw Error("Null input. (match_main)");
		return n = Math.max(0, Math.min(n, e.length)), e == t ? 0 : e.length ? e.substring(n, n + t.length) == t ? n : this.match_bitap_(e, t, n) : -1;
	}, t.prototype.match_bitap_ = function(e, t, n) {
		if (t.length > this.Match_MaxBits) throw Error("Pattern too long for this browser.");
		var r = this.match_alphabet_(t), i = this;
		function a(e, r) {
			var a = e / t.length, o = Math.abs(n - r);
			return i.Match_Distance ? a + o / i.Match_Distance : o ? 1 : a;
		}
		var o = this.Match_Threshold, s = e.indexOf(t, n);
		s != -1 && (o = Math.min(a(0, s), o), (s = e.lastIndexOf(t, n + t.length)) != -1 && (o = Math.min(a(0, s), o)));
		var c, l, u = 1 << t.length - 1;
		s = -1;
		for (var d, f = t.length + e.length, p = 0; p < t.length; p++) {
			for (c = 0, l = f; c < l;) a(p, n + l) <= o ? c = l : f = l, l = Math.floor((f - c) / 2 + c);
			f = l;
			var m = Math.max(1, n - l + 1), h = Math.min(n + l, e.length) + t.length, g = Array(h + 2);
			g[h + 1] = (1 << p) - 1;
			for (var _ = h; _ >= m; _--) {
				var v = r[e.charAt(_ - 1)];
				if (g[_] = p === 0 ? (g[_ + 1] << 1 | 1) & v : (g[_ + 1] << 1 | 1) & v | (d[_ + 1] | d[_]) << 1 | 1 | d[_ + 1], g[_] & u) {
					var y = a(p, _ - 1);
					if (y <= o) {
						if (o = y, !((s = _ - 1) > n)) break;
						m = Math.max(1, 2 * n - s);
					}
				}
			}
			if (a(p + 1, n) > o) break;
			d = g;
		}
		return s;
	}, t.prototype.match_alphabet_ = function(e) {
		for (var t = {}, n = 0; n < e.length; n++) t[e.charAt(n)] = 0;
		for (n = 0; n < e.length; n++) t[e.charAt(n)] |= 1 << e.length - n - 1;
		return t;
	}, t.prototype.patch_addContext_ = function(e, n) {
		if (n.length != 0) {
			if (e.start2 === null) throw Error("patch not initialized");
			for (var r = n.substring(e.start2, e.start2 + e.length1), i = 0; n.indexOf(r) != n.lastIndexOf(r) && r.length < this.Match_MaxBits - this.Patch_Margin - this.Patch_Margin;) i += this.Patch_Margin, r = n.substring(e.start2 - i, e.start2 + e.length1 + i);
			i += this.Patch_Margin;
			var a = n.substring(e.start2 - i, e.start2);
			a && e.diffs.unshift(new t.Diff(0, a));
			var o = n.substring(e.start2 + e.length1, e.start2 + e.length1 + i);
			o && e.diffs.push(new t.Diff(0, o)), e.start1 -= a.length, e.start2 -= a.length, e.length1 += a.length + o.length, e.length2 += a.length + o.length;
		}
	}, t.prototype.patch_make = function(e, n, r) {
		var i, a;
		if (typeof e == "string" && typeof n == "string" && r === void 0) i = e, (a = this.diff_main(i, n, !0)).length > 2 && (this.diff_cleanupSemantic(a), this.diff_cleanupEfficiency(a));
		else if (e && typeof e == "object" && n === void 0 && r === void 0) a = e, i = this.diff_text1(a);
		else if (typeof e == "string" && n && typeof n == "object" && r === void 0) i = e, a = n;
		else {
			if (typeof e != "string" || typeof n != "string" || !r || typeof r != "object") throw Error("Unknown call format to patch_make.");
			i = e, a = r;
		}
		if (a.length === 0) return [];
		for (var o = [], s = new t.patch_obj(), c = 0, l = 0, u = 0, d = i, f = i, p = 0; p < a.length; p++) {
			var m = a[p][0], h = a[p][1];
			switch (c || m === 0 || (s.start1 = l, s.start2 = u), m) {
				case 1:
					s.diffs[c++] = a[p], s.length2 += h.length, f = f.substring(0, u) + h + f.substring(u);
					break;
				case -1:
					s.length1 += h.length, s.diffs[c++] = a[p], f = f.substring(0, u) + f.substring(u + h.length);
					break;
				case 0: h.length <= 2 * this.Patch_Margin && c && a.length != p + 1 ? (s.diffs[c++] = a[p], s.length1 += h.length, s.length2 += h.length) : h.length >= 2 * this.Patch_Margin && c && (this.patch_addContext_(s, d), o.push(s), s = new t.patch_obj(), c = 0, d = f, l = u);
			}
			m !== 1 && (l += h.length), m !== -1 && (u += h.length);
		}
		return c && (this.patch_addContext_(s, d), o.push(s)), o;
	}, t.prototype.patch_deepCopy = function(e) {
		for (var n = [], r = 0; r < e.length; r++) {
			var i = e[r], a = new t.patch_obj();
			a.diffs = [];
			for (var o = 0; o < i.diffs.length; o++) a.diffs[o] = new t.Diff(i.diffs[o][0], i.diffs[o][1]);
			a.start1 = i.start1, a.start2 = i.start2, a.length1 = i.length1, a.length2 = i.length2, n[r] = a;
		}
		return n;
	}, t.prototype.patch_apply = function(e, t) {
		if (e.length == 0) return [t, []];
		e = this.patch_deepCopy(e);
		var n = this.patch_addPadding(e);
		t = n + t + n, this.patch_splitMax(e);
		for (var r = 0, i = [], a = 0; a < e.length; a++) {
			var o, s, c = e[a].start2 + r, l = this.diff_text1(e[a].diffs), u = -1;
			if (l.length > this.Match_MaxBits ? (o = this.match_main(t, l.substring(0, this.Match_MaxBits), c)) != -1 && ((u = this.match_main(t, l.substring(l.length - this.Match_MaxBits), c + l.length - this.Match_MaxBits)) == -1 || o >= u) && (o = -1) : o = this.match_main(t, l, c), o == -1) i[a] = !1, r -= e[a].length2 - e[a].length1;
			else if (i[a] = !0, r = o - c, l == (s = u == -1 ? t.substring(o, o + l.length) : t.substring(o, u + this.Match_MaxBits))) t = t.substring(0, o) + this.diff_text2(e[a].diffs) + t.substring(o + l.length);
			else {
				var d = this.diff_main(l, s, !1);
				if (l.length > this.Match_MaxBits && this.diff_levenshtein(d) / l.length > this.Patch_DeleteThreshold) i[a] = !1;
				else {
					this.diff_cleanupSemanticLossless(d);
					for (var f, p = 0, m = 0; m < e[a].diffs.length; m++) {
						var h = e[a].diffs[m];
						h[0] !== 0 && (f = this.diff_xIndex(d, p)), h[0] === 1 ? t = t.substring(0, o + f) + h[1] + t.substring(o + f) : h[0] === -1 && (t = t.substring(0, o + f) + t.substring(o + this.diff_xIndex(d, p + h[1].length))), h[0] !== -1 && (p += h[1].length);
					}
				}
			}
		}
		return [t = t.substring(n.length, t.length - n.length), i];
	}, t.prototype.patch_addPadding = function(e) {
		for (var n = this.Patch_Margin, r = "", i = 1; i <= n; i++) r += String.fromCharCode(i);
		for (i = 0; i < e.length; i++) e[i].start1 += n, e[i].start2 += n;
		var a = e[0], o = a.diffs;
		if (o.length == 0 || o[0][0] != 0) o.unshift(new t.Diff(0, r)), a.start1 -= n, a.start2 -= n, a.length1 += n, a.length2 += n;
		else if (n > o[0][1].length) {
			var s = n - o[0][1].length;
			o[0][1] = r.substring(o[0][1].length) + o[0][1], a.start1 -= s, a.start2 -= s, a.length1 += s, a.length2 += s;
		}
		return (o = (a = e[e.length - 1]).diffs).length == 0 || o[o.length - 1][0] != 0 ? (o.push(new t.Diff(0, r)), a.length1 += n, a.length2 += n) : n > o[o.length - 1][1].length && (s = n - o[o.length - 1][1].length, o[o.length - 1][1] += r.substring(0, s), a.length1 += s, a.length2 += s), r;
	}, t.prototype.patch_splitMax = function(e) {
		for (var n = this.Match_MaxBits, r = 0; r < e.length; r++) if (!(e[r].length1 <= n)) {
			var i = e[r];
			e.splice(r--, 1);
			for (var a = i.start1, o = i.start2, s = ""; i.diffs.length !== 0;) {
				var c = new t.patch_obj(), l = !0;
				for (c.start1 = a - s.length, c.start2 = o - s.length, s !== "" && (c.length1 = c.length2 = s.length, c.diffs.push(new t.Diff(0, s))); i.diffs.length !== 0 && c.length1 < n - this.Patch_Margin;) {
					var u = i.diffs[0][0], d = i.diffs[0][1];
					u === 1 ? (c.length2 += d.length, o += d.length, c.diffs.push(i.diffs.shift()), l = !1) : u === -1 && c.diffs.length == 1 && c.diffs[0][0] == 0 && d.length > 2 * n ? (c.length1 += d.length, a += d.length, l = !1, c.diffs.push(new t.Diff(u, d)), i.diffs.shift()) : (d = d.substring(0, n - c.length1 - this.Patch_Margin), c.length1 += d.length, a += d.length, u === 0 ? (c.length2 += d.length, o += d.length) : l = !1, c.diffs.push(new t.Diff(u, d)), d == i.diffs[0][1] ? i.diffs.shift() : i.diffs[0][1] = i.diffs[0][1].substring(d.length));
				}
				s = (s = this.diff_text2(c.diffs)).substring(s.length - this.Patch_Margin);
				var f = this.diff_text1(i.diffs).substring(0, this.Patch_Margin);
				f !== "" && (c.length1 += f.length, c.length2 += f.length, c.diffs.length !== 0 && c.diffs[c.diffs.length - 1][0] === 0 ? c.diffs[c.diffs.length - 1][1] += f : c.diffs.push(new t.Diff(0, f))), l || e.splice(++r, 0, c);
			}
		}
	}, t.prototype.patch_toText = function(e) {
		for (var t = [], n = 0; n < e.length; n++) t[n] = e[n];
		return t.join("");
	}, t.prototype.patch_fromText = function(e) {
		var n = [];
		if (!e) return n;
		for (var r = e.split("\n"), i = 0, a = /^@@ -(\d+),?(\d*) \+(\d+),?(\d*) @@$/; i < r.length;) {
			var o = r[i].match(a);
			if (!o) throw Error("Invalid patch string: " + r[i]);
			var s = new t.patch_obj();
			for (n.push(s), s.start1 = parseInt(o[1], 10), o[2] === "" ? (s.start1--, s.length1 = 1) : o[2] == "0" ? s.length1 = 0 : (s.start1--, s.length1 = parseInt(o[2], 10)), s.start2 = parseInt(o[3], 10), o[4] === "" ? (s.start2--, s.length2 = 1) : o[4] == "0" ? s.length2 = 0 : (s.start2--, s.length2 = parseInt(o[4], 10)), i++; i < r.length;) {
				var c = r[i].charAt(0);
				try {
					var l = decodeURI(r[i].substring(1));
				} catch {
					throw Error("Illegal escape in patch_fromText: " + l);
				}
				if (c == "-") s.diffs.push(new t.Diff(-1, l));
				else if (c == "+") s.diffs.push(new t.Diff(1, l));
				else if (c == " ") s.diffs.push(new t.Diff(0, l));
				else {
					if (c == "@") break;
					if (c !== "") throw Error("Invalid patch mode \"" + c + "\" in: " + l);
				}
				i++;
			}
		}
		return n;
	}, (t.patch_obj = function() {
		this.diffs = [], this.start1 = null, this.start2 = null, this.length1 = 0, this.length2 = 0;
	}).prototype.toString = function() {
		for (var e, t = ["@@ -" + (this.length1 === 0 ? this.start1 + ",0" : this.length1 == 1 ? this.start1 + 1 : this.start1 + 1 + "," + this.length1) + " +" + (this.length2 === 0 ? this.start2 + ",0" : this.length2 == 1 ? this.start2 + 1 : this.start2 + 1 + "," + this.length2) + " @@\n"], n = 0; n < this.diffs.length; n++) {
			switch (this.diffs[n][0]) {
				case 1:
					e = "+";
					break;
				case -1:
					e = "-";
					break;
				case 0: e = " ";
			}
			t[n + 1] = e + encodeURI(this.diffs[n][1]) + "\n";
		}
		return t.join("").replace(/%20/g, " ");
	}, e.exports = t, e.exports.diff_match_patch = t, e.exports.DIFF_DELETE = -1, e.exports.DIFF_INSERT = 1, e.exports.DIFF_EQUAL = 0;
})), Qi = Zi.DIFF_EQUAL, $i = Zi.DIFF_DELETE, ea = Zi.DIFF_INSERT;
function ta(e) {
	var t = Xi(e, (function(e) {
		return !ie(e);
	}));
	if (t === -1) return [];
	var n = Xi(e, (function(e) {
		return !!ie(e);
	}), t);
	return n === -1 ? [e.slice(t)] : [e.slice(t, n)].concat(S(ta(e.slice(n))));
}
function na(e) {
	return e.reduce((function(e, t) {
		var n = b(t, 2), r = n[0], i = x(n[1].split("\n").map((function(e) {
			return [r, e];
		}))), a = i[0], o = i.slice(1);
		return [].concat(S(e.slice(0, -1)), [[].concat(S(e[e.length - 1]), [a])], S(o.map((function(e) {
			return [e];
		}))));
	}), [[]]);
}
function ra(e, t) {
	return e.reduce((function(e, n) {
		var r = b(e, 2), i = r[0], a = r[1], o = b(n, 2), s = o[0], c = o[1];
		if (s !== Qi) {
			var l = {
				type: "edit",
				lineNumber: t,
				start: a,
				length: c.length
			};
			i.push(l);
		}
		return [i, a + c.length];
	}), [[], 0])[0];
}
function ia(e, t) {
	return Ci(e, (function(e, n) {
		return ra(e, t + n);
	}));
}
function aa(e, t) {
	var n = new Zi(), r = n.diff_main(e, t);
	return n.diff_cleanupSemantic(r), r.length <= 1 ? [[], []] : function(e) {
		return e.reduce((function(e, t) {
			var n = b(e, 2), r = n[0], i = n[1];
			switch (b(t, 1)[0]) {
				case ea:
					i.push(t);
					break;
				case $i:
					r.push(t);
					break;
				default: r.push(t), i.push(t);
			}
			return [r, i];
		}), [[], []]);
	}(r);
}
function oa(e) {
	var t = b(e.reduce((function(e, t) {
		var n = b(e, 2), r = n[0], i = n[1];
		return re(t) ? [r + (r ? "\n" : "") + t.content, i] : [r, i + (i ? "\n" : "") + t.content];
	}), ["", ""]), 2), n = b(aa(t[0], t[1]), 2), r = n[0], i = n[1];
	if (r.length === 0 && i.length === 0) return [[], []];
	var a = function(e) {
		if (e && !ie(e)) return e.lineNumber;
	}, o = a(e.find(re)), s = a(e.find(P));
	if (o === void 0 || s === void 0) throw Error("Could not find start line number for edit");
	return [ia(na(r), o), ia(na(i), s)];
}
function sa(e) {
	var t = b(e.reduce((function(e, t) {
		var n = b(e, 3), r = n[0], i = n[1], a = n[2];
		if (!a || !re(a) || !P(t)) return [
			r,
			i,
			t
		];
		var o = b(aa(a.content, t.content), 2), s = o[0], c = o[1];
		return [
			r.concat(ra(s, a.lineNumber)),
			i.concat(ra(c, t.lineNumber)),
			t
		];
	}), [
		[],
		[],
		null
	]), 2);
	return [t[0], t[1]];
}
function ca(e) {
	var t = (arguments.length > 1 && arguments[1] !== void 0 ? arguments[1] : {}).type, n = (t === void 0 ? "block" : t) === "block" ? oa : sa, r = b(Ci(e.map((function(e) {
		return e.changes;
	})), ta).map(n).reduce((function(e, t) {
		var n = b(e, 2), r = n[0], i = n[1], a = b(t, 2), o = a[0], s = a[1];
		return [r.concat(o), i.concat(s)];
	}), [[], []]), 2), i = r[0], a = r[1];
	return qi(Ji(i), Ji(a));
}
var la = ["enhancers"], ua = function(e) {
	var t, n = arguments.length > 1 && arguments[1] !== void 0 ? arguments[1] : {}, r = n.enhancers, i = r === void 0 ? [] : r, a = b(Oi(e, y(n, la)), 2), o = a[0], s = a[1], c = [Fi(o), Fi(s)], l = b((t = [c[0], c[1]], i.reduce((function(e, t) {
		return t(e);
	}), t)), 2), u = l[0], d = l[1], f = [u.map(Bi), d.map(Bi)], p = f[1];
	return {
		old: f[0].map((function(e) {
			return e.children ?? [];
		})),
		new: p.map((function(e) {
			return e.children ?? [];
		}))
	};
}, da = class {
	constructor(e, t, n) {
		this.normal = t, this.property = e, n && (this.space = n);
	}
};
da.prototype.normal = {}, da.prototype.property = {}, da.prototype.space = void 0;
//#endregion
//#region node_modules/property-information/lib/util/merge.js
function fa(e, t) {
	let n = {}, r = {};
	for (let t of e) Object.assign(n, t.property), Object.assign(r, t.normal);
	return new da(n, r, t);
}
//#endregion
//#region node_modules/property-information/lib/normalize.js
function pa(e) {
	return e.toLowerCase();
}
//#endregion
//#region node_modules/property-information/lib/util/info.js
var ma = class {
	constructor(e, t) {
		this.attribute = t, this.property = e;
	}
};
ma.prototype.attribute = "", ma.prototype.booleanish = !1, ma.prototype.boolean = !1, ma.prototype.commaOrSpaceSeparated = !1, ma.prototype.commaSeparated = !1, ma.prototype.defined = !1, ma.prototype.mustUseProperty = !1, ma.prototype.number = !1, ma.prototype.overloadedBoolean = !1, ma.prototype.property = "", ma.prototype.spaceSeparated = !1, ma.prototype.space = void 0;
//#endregion
//#region node_modules/property-information/lib/util/types.js
var ha = /* @__PURE__ */ n({
	boolean: () => V,
	booleanish: () => _a,
	commaOrSpaceSeparated: () => ba,
	commaSeparated: () => ya,
	number: () => H,
	overloadedBoolean: () => va,
	spaceSeparated: () => U
}), ga = 0, V = xa(), _a = xa(), va = xa(), H = xa(), U = xa(), ya = xa(), ba = xa();
function xa() {
	return 2 ** ++ga;
}
//#endregion
//#region node_modules/property-information/lib/util/defined-info.js
var Sa = Object.keys(ha), Ca = class extends ma {
	constructor(e, t, n, r) {
		let i = -1;
		if (super(e, t), wa(this, "space", r), typeof n == "number") for (; ++i < Sa.length;) {
			let e = Sa[i];
			wa(this, Sa[i], (n & ha[e]) === ha[e]);
		}
	}
};
Ca.prototype.defined = !0;
function wa(e, t, n) {
	n && (e[t] = n);
}
//#endregion
//#region node_modules/property-information/lib/util/create.js
function Ta(e) {
	let t = {}, n = {};
	for (let [r, i] of Object.entries(e.properties)) {
		let a = new Ca(r, e.transform(e.attributes || {}, r), i, e.space);
		e.mustUseProperty && e.mustUseProperty.includes(r) && (a.mustUseProperty = !0), t[r] = a, n[pa(r)] = r, n[pa(a.attribute)] = r;
	}
	return new da(t, n, e.space);
}
//#endregion
//#region node_modules/property-information/lib/aria.js
var Ea = Ta({
	properties: {
		ariaActiveDescendant: null,
		ariaAtomic: _a,
		ariaAutoComplete: null,
		ariaBusy: _a,
		ariaChecked: _a,
		ariaColCount: H,
		ariaColIndex: H,
		ariaColSpan: H,
		ariaControls: U,
		ariaCurrent: null,
		ariaDescribedBy: U,
		ariaDetails: null,
		ariaDisabled: _a,
		ariaDropEffect: U,
		ariaErrorMessage: null,
		ariaExpanded: _a,
		ariaFlowTo: U,
		ariaGrabbed: _a,
		ariaHasPopup: null,
		ariaHidden: _a,
		ariaInvalid: null,
		ariaKeyShortcuts: null,
		ariaLabel: null,
		ariaLabelledBy: U,
		ariaLevel: H,
		ariaLive: null,
		ariaModal: _a,
		ariaMultiLine: _a,
		ariaMultiSelectable: _a,
		ariaOrientation: null,
		ariaOwns: U,
		ariaPlaceholder: null,
		ariaPosInSet: H,
		ariaPressed: _a,
		ariaReadOnly: _a,
		ariaRelevant: null,
		ariaRequired: _a,
		ariaRoleDescription: U,
		ariaRowCount: H,
		ariaRowIndex: H,
		ariaRowSpan: H,
		ariaSelected: _a,
		ariaSetSize: H,
		ariaSort: null,
		ariaValueMax: H,
		ariaValueMin: H,
		ariaValueNow: H,
		ariaValueText: null,
		role: null
	},
	transform(e, t) {
		return t === "role" ? t : "aria-" + t.slice(4).toLowerCase();
	}
});
//#endregion
//#region node_modules/property-information/lib/util/case-sensitive-transform.js
function Da(e, t) {
	return t in e ? e[t] : t;
}
//#endregion
//#region node_modules/property-information/lib/util/case-insensitive-transform.js
function Oa(e, t) {
	return Da(e, t.toLowerCase());
}
//#endregion
//#region node_modules/property-information/lib/html.js
var ka = Ta({
	attributes: {
		acceptcharset: "accept-charset",
		classname: "class",
		htmlfor: "for",
		httpequiv: "http-equiv"
	},
	mustUseProperty: [
		"checked",
		"multiple",
		"muted",
		"selected"
	],
	properties: {
		abbr: null,
		accept: ya,
		acceptCharset: U,
		accessKey: U,
		action: null,
		allow: null,
		allowFullScreen: V,
		allowPaymentRequest: V,
		allowUserMedia: V,
		alpha: V,
		alt: null,
		as: null,
		async: V,
		autoCapitalize: null,
		autoComplete: U,
		autoFocus: V,
		autoPlay: V,
		blocking: U,
		capture: null,
		charSet: null,
		checked: V,
		cite: null,
		className: U,
		closedBy: null,
		colorSpace: null,
		cols: H,
		colSpan: H,
		command: null,
		commandFor: null,
		content: null,
		contentEditable: _a,
		controls: V,
		controlsList: U,
		coords: H | ya,
		crossOrigin: null,
		data: null,
		dateTime: null,
		decoding: null,
		default: V,
		defer: V,
		dir: null,
		dirName: null,
		disabled: V,
		download: va,
		draggable: _a,
		encType: null,
		enterKeyHint: null,
		fetchPriority: null,
		form: null,
		formAction: null,
		formEncType: null,
		formMethod: null,
		formNoValidate: V,
		formTarget: null,
		headers: U,
		height: H,
		hidden: va,
		high: H,
		href: null,
		hrefLang: null,
		htmlFor: U,
		httpEquiv: U,
		id: null,
		imageSizes: null,
		imageSrcSet: null,
		inert: V,
		inputMode: null,
		integrity: null,
		is: null,
		isMap: V,
		itemId: null,
		itemProp: U,
		itemRef: U,
		itemScope: V,
		itemType: U,
		kind: null,
		label: null,
		lang: null,
		language: null,
		list: null,
		loading: null,
		loop: V,
		low: H,
		manifest: null,
		max: null,
		maxLength: H,
		media: null,
		method: null,
		min: null,
		minLength: H,
		multiple: V,
		muted: V,
		name: null,
		nonce: null,
		noModule: V,
		noValidate: V,
		onAbort: null,
		onAfterPrint: null,
		onAuxClick: null,
		onBeforeMatch: null,
		onBeforePrint: null,
		onBeforeToggle: null,
		onBeforeUnload: null,
		onBlur: null,
		onCancel: null,
		onCanPlay: null,
		onCanPlayThrough: null,
		onChange: null,
		onClick: null,
		onClose: null,
		onContextLost: null,
		onContextMenu: null,
		onContextRestored: null,
		onCopy: null,
		onCueChange: null,
		onCut: null,
		onDblClick: null,
		onDrag: null,
		onDragEnd: null,
		onDragEnter: null,
		onDragExit: null,
		onDragLeave: null,
		onDragOver: null,
		onDragStart: null,
		onDrop: null,
		onDurationChange: null,
		onEmptied: null,
		onEnded: null,
		onError: null,
		onFocus: null,
		onFormData: null,
		onHashChange: null,
		onInput: null,
		onInvalid: null,
		onKeyDown: null,
		onKeyPress: null,
		onKeyUp: null,
		onLanguageChange: null,
		onLoad: null,
		onLoadedData: null,
		onLoadedMetadata: null,
		onLoadEnd: null,
		onLoadStart: null,
		onMessage: null,
		onMessageError: null,
		onMouseDown: null,
		onMouseEnter: null,
		onMouseLeave: null,
		onMouseMove: null,
		onMouseOut: null,
		onMouseOver: null,
		onMouseUp: null,
		onOffline: null,
		onOnline: null,
		onPageHide: null,
		onPageShow: null,
		onPaste: null,
		onPause: null,
		onPlay: null,
		onPlaying: null,
		onPopState: null,
		onProgress: null,
		onRateChange: null,
		onRejectionHandled: null,
		onReset: null,
		onResize: null,
		onScroll: null,
		onScrollEnd: null,
		onSecurityPolicyViolation: null,
		onSeeked: null,
		onSeeking: null,
		onSelect: null,
		onSlotChange: null,
		onStalled: null,
		onStorage: null,
		onSubmit: null,
		onSuspend: null,
		onTimeUpdate: null,
		onToggle: null,
		onUnhandledRejection: null,
		onUnload: null,
		onVolumeChange: null,
		onWaiting: null,
		onWheel: null,
		open: V,
		optimum: H,
		pattern: null,
		ping: U,
		placeholder: null,
		playsInline: V,
		popover: null,
		popoverTarget: null,
		popoverTargetAction: null,
		poster: null,
		preload: null,
		readOnly: V,
		referrerPolicy: null,
		rel: U,
		required: V,
		reversed: V,
		rows: H,
		rowSpan: H,
		sandbox: U,
		scope: null,
		scoped: V,
		seamless: V,
		selected: V,
		shadowRootClonable: V,
		shadowRootCustomElementRegistry: V,
		shadowRootDelegatesFocus: V,
		shadowRootMode: null,
		shadowRootSerializable: V,
		shape: null,
		size: H,
		sizes: null,
		slot: null,
		span: H,
		spellCheck: _a,
		src: null,
		srcDoc: null,
		srcLang: null,
		srcSet: null,
		start: H,
		step: null,
		style: null,
		tabIndex: H,
		target: null,
		title: null,
		translate: null,
		type: null,
		typeMustMatch: V,
		useMap: null,
		value: _a,
		width: H,
		wrap: null,
		writingSuggestions: null,
		align: null,
		aLink: null,
		archive: U,
		axis: null,
		background: null,
		bgColor: null,
		border: H,
		borderColor: null,
		bottomMargin: H,
		cellPadding: null,
		cellSpacing: null,
		char: null,
		charOff: null,
		classId: null,
		clear: null,
		code: null,
		codeBase: null,
		codeType: null,
		color: null,
		compact: V,
		declare: V,
		event: null,
		face: null,
		frame: null,
		frameBorder: null,
		hSpace: H,
		leftMargin: H,
		link: null,
		longDesc: null,
		lowSrc: null,
		marginHeight: H,
		marginWidth: H,
		noResize: V,
		noHref: V,
		noShade: V,
		noWrap: V,
		object: null,
		profile: null,
		prompt: null,
		rev: null,
		rightMargin: H,
		rules: null,
		scheme: null,
		scrolling: _a,
		standby: null,
		summary: null,
		text: null,
		topMargin: H,
		valueType: null,
		version: null,
		vAlign: null,
		vLink: null,
		vSpace: H,
		allowTransparency: null,
		autoCorrect: null,
		autoSave: null,
		credentialless: V,
		disablePictureInPicture: V,
		disableRemotePlayback: V,
		exportParts: ya,
		part: U,
		prefix: null,
		property: null,
		results: H,
		security: null,
		unselectable: null
	},
	space: "html",
	transform: Oa
}), Aa = Ta({
	attributes: {
		accentHeight: "accent-height",
		alignmentBaseline: "alignment-baseline",
		arabicForm: "arabic-form",
		baselineShift: "baseline-shift",
		capHeight: "cap-height",
		className: "class",
		clipPath: "clip-path",
		clipRule: "clip-rule",
		colorInterpolation: "color-interpolation",
		colorInterpolationFilters: "color-interpolation-filters",
		colorProfile: "color-profile",
		colorRendering: "color-rendering",
		crossOrigin: "crossorigin",
		dataType: "datatype",
		dominantBaseline: "dominant-baseline",
		enableBackground: "enable-background",
		fillOpacity: "fill-opacity",
		fillRule: "fill-rule",
		floodColor: "flood-color",
		floodOpacity: "flood-opacity",
		fontFamily: "font-family",
		fontSize: "font-size",
		fontSizeAdjust: "font-size-adjust",
		fontStretch: "font-stretch",
		fontStyle: "font-style",
		fontVariant: "font-variant",
		fontWeight: "font-weight",
		glyphName: "glyph-name",
		glyphOrientationHorizontal: "glyph-orientation-horizontal",
		glyphOrientationVertical: "glyph-orientation-vertical",
		hrefLang: "hreflang",
		horizAdvX: "horiz-adv-x",
		horizOriginX: "horiz-origin-x",
		horizOriginY: "horiz-origin-y",
		imageRendering: "image-rendering",
		letterSpacing: "letter-spacing",
		lightingColor: "lighting-color",
		markerEnd: "marker-end",
		markerMid: "marker-mid",
		markerStart: "marker-start",
		maskType: "mask-type",
		navDown: "nav-down",
		navDownLeft: "nav-down-left",
		navDownRight: "nav-down-right",
		navLeft: "nav-left",
		navNext: "nav-next",
		navPrev: "nav-prev",
		navRight: "nav-right",
		navUp: "nav-up",
		navUpLeft: "nav-up-left",
		navUpRight: "nav-up-right",
		onAbort: "onabort",
		onActivate: "onactivate",
		onAfterPrint: "onafterprint",
		onBeforePrint: "onbeforeprint",
		onBegin: "onbegin",
		onCancel: "oncancel",
		onCanPlay: "oncanplay",
		onCanPlayThrough: "oncanplaythrough",
		onChange: "onchange",
		onClick: "onclick",
		onClose: "onclose",
		onCopy: "oncopy",
		onCueChange: "oncuechange",
		onCut: "oncut",
		onDblClick: "ondblclick",
		onDrag: "ondrag",
		onDragEnd: "ondragend",
		onDragEnter: "ondragenter",
		onDragExit: "ondragexit",
		onDragLeave: "ondragleave",
		onDragOver: "ondragover",
		onDragStart: "ondragstart",
		onDrop: "ondrop",
		onDurationChange: "ondurationchange",
		onEmptied: "onemptied",
		onEnd: "onend",
		onEnded: "onended",
		onError: "onerror",
		onFocus: "onfocus",
		onFocusIn: "onfocusin",
		onFocusOut: "onfocusout",
		onHashChange: "onhashchange",
		onInput: "oninput",
		onInvalid: "oninvalid",
		onKeyDown: "onkeydown",
		onKeyPress: "onkeypress",
		onKeyUp: "onkeyup",
		onLoad: "onload",
		onLoadedData: "onloadeddata",
		onLoadedMetadata: "onloadedmetadata",
		onLoadStart: "onloadstart",
		onMessage: "onmessage",
		onMouseDown: "onmousedown",
		onMouseEnter: "onmouseenter",
		onMouseLeave: "onmouseleave",
		onMouseMove: "onmousemove",
		onMouseOut: "onmouseout",
		onMouseOver: "onmouseover",
		onMouseUp: "onmouseup",
		onMouseWheel: "onmousewheel",
		onOffline: "onoffline",
		onOnline: "ononline",
		onPageHide: "onpagehide",
		onPageShow: "onpageshow",
		onPaste: "onpaste",
		onPause: "onpause",
		onPlay: "onplay",
		onPlaying: "onplaying",
		onPopState: "onpopstate",
		onProgress: "onprogress",
		onRateChange: "onratechange",
		onRepeat: "onrepeat",
		onReset: "onreset",
		onResize: "onresize",
		onScroll: "onscroll",
		onSeeked: "onseeked",
		onSeeking: "onseeking",
		onSelect: "onselect",
		onShow: "onshow",
		onStalled: "onstalled",
		onStorage: "onstorage",
		onSubmit: "onsubmit",
		onSuspend: "onsuspend",
		onTimeUpdate: "ontimeupdate",
		onToggle: "ontoggle",
		onUnload: "onunload",
		onVolumeChange: "onvolumechange",
		onWaiting: "onwaiting",
		onZoom: "onzoom",
		overlinePosition: "overline-position",
		overlineThickness: "overline-thickness",
		paintOrder: "paint-order",
		panose1: "panose-1",
		pointerEvents: "pointer-events",
		referrerPolicy: "referrerpolicy",
		renderingIntent: "rendering-intent",
		shapeRendering: "shape-rendering",
		stopColor: "stop-color",
		stopOpacity: "stop-opacity",
		strikethroughPosition: "strikethrough-position",
		strikethroughThickness: "strikethrough-thickness",
		strokeDashArray: "stroke-dasharray",
		strokeDashOffset: "stroke-dashoffset",
		strokeLineCap: "stroke-linecap",
		strokeLineJoin: "stroke-linejoin",
		strokeMiterLimit: "stroke-miterlimit",
		strokeOpacity: "stroke-opacity",
		strokeWidth: "stroke-width",
		tabIndex: "tabindex",
		textAnchor: "text-anchor",
		textDecoration: "text-decoration",
		textRendering: "text-rendering",
		transformOrigin: "transform-origin",
		typeOf: "typeof",
		underlinePosition: "underline-position",
		underlineThickness: "underline-thickness",
		unicodeBidi: "unicode-bidi",
		unicodeRange: "unicode-range",
		unitsPerEm: "units-per-em",
		vAlphabetic: "v-alphabetic",
		vHanging: "v-hanging",
		vIdeographic: "v-ideographic",
		vMathematical: "v-mathematical",
		vectorEffect: "vector-effect",
		vertAdvY: "vert-adv-y",
		vertOriginX: "vert-origin-x",
		vertOriginY: "vert-origin-y",
		wordSpacing: "word-spacing",
		writingMode: "writing-mode",
		xHeight: "x-height",
		playbackOrder: "playbackorder",
		timelineBegin: "timelinebegin"
	},
	properties: {
		about: ba,
		accentHeight: H,
		accumulate: null,
		additive: null,
		alignmentBaseline: null,
		alphabetic: H,
		amplitude: H,
		arabicForm: null,
		ascent: H,
		attributeName: null,
		attributeType: null,
		azimuth: H,
		bandwidth: null,
		baselineShift: null,
		baseFrequency: null,
		baseProfile: null,
		bbox: null,
		begin: null,
		bias: H,
		by: null,
		calcMode: null,
		capHeight: H,
		className: U,
		clip: null,
		clipPath: null,
		clipPathUnits: null,
		clipRule: null,
		color: null,
		colorInterpolation: null,
		colorInterpolationFilters: null,
		colorProfile: null,
		colorRendering: null,
		content: null,
		contentScriptType: null,
		contentStyleType: null,
		crossOrigin: null,
		cursor: null,
		cx: null,
		cy: null,
		d: null,
		dataType: null,
		defaultAction: null,
		descent: H,
		diffuseConstant: H,
		direction: null,
		display: null,
		dur: null,
		divisor: H,
		dominantBaseline: null,
		download: V,
		dx: null,
		dy: null,
		edgeMode: null,
		editable: null,
		elevation: H,
		enableBackground: null,
		end: null,
		event: null,
		exponent: H,
		externalResourcesRequired: null,
		fill: null,
		fillOpacity: H,
		fillRule: null,
		filter: null,
		filterRes: null,
		filterUnits: null,
		floodColor: null,
		floodOpacity: null,
		focusable: null,
		focusHighlight: null,
		fontFamily: null,
		fontSize: null,
		fontSizeAdjust: null,
		fontStretch: null,
		fontStyle: null,
		fontVariant: null,
		fontWeight: null,
		format: null,
		fr: null,
		from: null,
		fx: null,
		fy: null,
		g1: ya,
		g2: ya,
		glyphName: ya,
		glyphOrientationHorizontal: null,
		glyphOrientationVertical: null,
		glyphRef: null,
		gradientTransform: null,
		gradientUnits: null,
		handler: null,
		hanging: H,
		hatchContentUnits: null,
		hatchUnits: null,
		height: null,
		href: null,
		hrefLang: null,
		horizAdvX: H,
		horizOriginX: H,
		horizOriginY: H,
		id: null,
		ideographic: H,
		imageRendering: null,
		initialVisibility: null,
		in: null,
		in2: null,
		intercept: H,
		k: H,
		k1: H,
		k2: H,
		k3: H,
		k4: H,
		kernelMatrix: ba,
		kernelUnitLength: null,
		keyPoints: null,
		keySplines: null,
		keyTimes: null,
		kerning: null,
		lang: null,
		lengthAdjust: null,
		letterSpacing: null,
		lightingColor: null,
		limitingConeAngle: H,
		local: null,
		markerEnd: null,
		markerMid: null,
		markerStart: null,
		markerHeight: null,
		markerUnits: null,
		markerWidth: null,
		mask: null,
		maskContentUnits: null,
		maskType: null,
		maskUnits: null,
		mathematical: null,
		max: null,
		media: null,
		mediaCharacterEncoding: null,
		mediaContentEncodings: null,
		mediaSize: H,
		mediaTime: null,
		method: null,
		min: null,
		mode: null,
		name: null,
		navDown: null,
		navDownLeft: null,
		navDownRight: null,
		navLeft: null,
		navNext: null,
		navPrev: null,
		navRight: null,
		navUp: null,
		navUpLeft: null,
		navUpRight: null,
		numOctaves: null,
		observer: null,
		offset: null,
		onAbort: null,
		onActivate: null,
		onAfterPrint: null,
		onBeforePrint: null,
		onBegin: null,
		onCancel: null,
		onCanPlay: null,
		onCanPlayThrough: null,
		onChange: null,
		onClick: null,
		onClose: null,
		onCopy: null,
		onCueChange: null,
		onCut: null,
		onDblClick: null,
		onDrag: null,
		onDragEnd: null,
		onDragEnter: null,
		onDragExit: null,
		onDragLeave: null,
		onDragOver: null,
		onDragStart: null,
		onDrop: null,
		onDurationChange: null,
		onEmptied: null,
		onEnd: null,
		onEnded: null,
		onError: null,
		onFocus: null,
		onFocusIn: null,
		onFocusOut: null,
		onHashChange: null,
		onInput: null,
		onInvalid: null,
		onKeyDown: null,
		onKeyPress: null,
		onKeyUp: null,
		onLoad: null,
		onLoadedData: null,
		onLoadedMetadata: null,
		onLoadStart: null,
		onMessage: null,
		onMouseDown: null,
		onMouseEnter: null,
		onMouseLeave: null,
		onMouseMove: null,
		onMouseOut: null,
		onMouseOver: null,
		onMouseUp: null,
		onMouseWheel: null,
		onOffline: null,
		onOnline: null,
		onPageHide: null,
		onPageShow: null,
		onPaste: null,
		onPause: null,
		onPlay: null,
		onPlaying: null,
		onPopState: null,
		onProgress: null,
		onRateChange: null,
		onRepeat: null,
		onReset: null,
		onResize: null,
		onScroll: null,
		onSeeked: null,
		onSeeking: null,
		onSelect: null,
		onShow: null,
		onStalled: null,
		onStorage: null,
		onSubmit: null,
		onSuspend: null,
		onTimeUpdate: null,
		onToggle: null,
		onUnload: null,
		onVolumeChange: null,
		onWaiting: null,
		onZoom: null,
		opacity: null,
		operator: null,
		order: null,
		orient: null,
		orientation: null,
		origin: null,
		overflow: null,
		overlay: null,
		overlinePosition: H,
		overlineThickness: H,
		paintOrder: null,
		panose1: null,
		path: null,
		pathLength: H,
		patternContentUnits: null,
		patternTransform: null,
		patternUnits: null,
		phase: null,
		ping: U,
		pitch: null,
		playbackOrder: null,
		pointerEvents: null,
		points: null,
		pointsAtX: H,
		pointsAtY: H,
		pointsAtZ: H,
		preserveAlpha: null,
		preserveAspectRatio: null,
		primitiveUnits: null,
		propagate: null,
		property: ba,
		r: null,
		radius: null,
		referrerPolicy: null,
		refX: null,
		refY: null,
		rel: ba,
		rev: ba,
		renderingIntent: null,
		repeatCount: null,
		repeatDur: null,
		requiredExtensions: ba,
		requiredFeatures: ba,
		requiredFonts: ba,
		requiredFormats: ba,
		resource: null,
		restart: null,
		result: null,
		rotate: null,
		rx: null,
		ry: null,
		scale: null,
		seed: null,
		shapeRendering: null,
		side: null,
		slope: null,
		snapshotTime: null,
		specularConstant: H,
		specularExponent: H,
		spreadMethod: null,
		spacing: null,
		startOffset: null,
		stdDeviation: null,
		stemh: null,
		stemv: null,
		stitchTiles: null,
		stopColor: null,
		stopOpacity: null,
		strikethroughPosition: H,
		strikethroughThickness: H,
		string: null,
		stroke: null,
		strokeDashArray: ba,
		strokeDashOffset: null,
		strokeLineCap: null,
		strokeLineJoin: null,
		strokeMiterLimit: H,
		strokeOpacity: H,
		strokeWidth: null,
		style: null,
		surfaceScale: H,
		syncBehavior: null,
		syncBehaviorDefault: null,
		syncMaster: null,
		syncTolerance: null,
		syncToleranceDefault: null,
		systemLanguage: ba,
		tabIndex: H,
		tableValues: null,
		target: null,
		targetX: H,
		targetY: H,
		textAnchor: null,
		textDecoration: null,
		textRendering: null,
		textLength: null,
		timelineBegin: null,
		title: null,
		transformBehavior: null,
		type: null,
		typeOf: ba,
		to: null,
		transform: null,
		transformOrigin: null,
		u1: null,
		u2: null,
		underlinePosition: H,
		underlineThickness: H,
		unicode: null,
		unicodeBidi: null,
		unicodeRange: null,
		unitsPerEm: H,
		values: null,
		vAlphabetic: H,
		vMathematical: H,
		vectorEffect: null,
		vHanging: H,
		vIdeographic: H,
		version: null,
		vertAdvY: H,
		vertOriginX: H,
		vertOriginY: H,
		viewBox: null,
		viewTarget: null,
		visibility: null,
		width: null,
		widths: null,
		wordSpacing: null,
		writingMode: null,
		x: null,
		x1: null,
		x2: null,
		xChannelSelector: null,
		xHeight: H,
		y: null,
		y1: null,
		y2: null,
		yChannelSelector: null,
		z: null,
		zoomAndPan: null
	},
	space: "svg",
	transform: Da
}), ja = Ta({
	properties: {
		xLinkActuate: null,
		xLinkArcRole: null,
		xLinkHref: null,
		xLinkRole: null,
		xLinkShow: null,
		xLinkTitle: null,
		xLinkType: null
	},
	space: "xlink",
	transform(e, t) {
		return "xlink:" + t.slice(5).toLowerCase();
	}
}), Ma = Ta({
	attributes: { xmlnsxlink: "xmlns:xlink" },
	properties: {
		xmlnsXLink: null,
		xmlns: null
	},
	space: "xmlns",
	transform: Oa
}), Na = Ta({
	properties: {
		xmlBase: null,
		xmlLang: null,
		xmlSpace: null
	},
	space: "xml",
	transform(e, t) {
		return "xml:" + t.slice(3).toLowerCase();
	}
}), Pa = /[A-Z]/g, Fa = /-[a-z]/g, Ia = /^data[-\w.:]+$/i;
function La(e, t) {
	let n = pa(t), r = t, i = ma;
	if (n in e.normal) return e.property[e.normal[n]];
	if (n.length > 4 && n.slice(0, 4) === "data" && Ia.test(t)) {
		if (t.charAt(4) === "-") {
			let e = t.slice(5).replace(Fa, za);
			r = "data" + e.charAt(0).toUpperCase() + e.slice(1);
		} else {
			let e = t.slice(4);
			if (!Fa.test(e)) {
				let n = e.replace(Pa, Ra);
				n.charAt(0) !== "-" && (n = "-" + n), t = "data" + n;
			}
		}
		i = Ca;
	}
	return new i(r, t);
}
function Ra(e) {
	return "-" + e.toLowerCase();
}
function za(e) {
	return e.charAt(1).toUpperCase();
}
//#endregion
//#region node_modules/property-information/index.js
var Ba = fa([
	Ea,
	ka,
	ja,
	Ma,
	Na
], "html"), Va = fa([
	Ea,
	Aa,
	ja,
	Ma,
	Na
], "svg");
//#endregion
//#region node_modules/comma-separated-tokens/index.js
function Ha(e) {
	let t = [], n = String(e || ""), r = n.indexOf(","), i = 0, a = !1;
	for (; !a;) {
		r === -1 && (r = n.length, a = !0);
		let e = n.slice(i, r).trim();
		(e || !a) && t.push(e), i = r + 1, r = n.indexOf(",", i);
	}
	return t;
}
//#endregion
//#region node_modules/hast-util-parse-selector/lib/index.js
var Ua = /[#.]/g;
function Wa(e, t) {
	let n = e || "", r = {}, i = 0, a, o;
	for (; i < n.length;) {
		Ua.lastIndex = i;
		let e = Ua.exec(n), t = n.slice(i, e ? e.index : n.length);
		t && (a ? a === "#" ? r.id = t : Array.isArray(r.className) ? r.className.push(t) : r.className = [t] : o = t, i += t.length), e && (a = e[0], i++);
	}
	return {
		type: "element",
		tagName: o || t || "div",
		properties: r,
		children: []
	};
}
//#endregion
//#region node_modules/space-separated-tokens/index.js
function Ga(e) {
	let t = String(e || "").trim();
	return t ? t.split(/[ \t\n\r\f]+/g) : [];
}
//#endregion
//#region node_modules/hastscript/lib/create-h.js
function Ka(e, t, n) {
	let r = n ? Qa(n) : void 0;
	function i(n, i, ...a) {
		let o;
		if (n == null) {
			o = {
				type: "root",
				children: []
			};
			let e = i;
			a.unshift(e);
		} else {
			o = Wa(n, t);
			let s = o.tagName.toLowerCase(), c = r ? r.get(s) : void 0;
			if (o.tagName = c || s, qa(i)) a.unshift(i);
			else for (let [t, n] of Object.entries(i)) Ja(e, o.properties, t, n);
		}
		for (let e of a) Ya(o.children, e);
		return o.type === "element" && o.tagName === "template" && (o.content = {
			type: "root",
			children: o.children
		}, o.children = []), o;
	}
	return i;
}
function qa(e) {
	if (typeof e != "object" || !e || Array.isArray(e)) return !0;
	if (typeof e.type != "string") return !1;
	let t = e, n = Object.keys(e);
	for (let e of n) {
		let n = t[e];
		if (n && typeof n == "object") {
			if (!Array.isArray(n)) return !0;
			let e = n;
			for (let t of e) if (typeof t != "number" && typeof t != "string") return !0;
		}
	}
	return !!("children" in e && Array.isArray(e.children));
}
function Ja(e, t, n, r) {
	let i = La(e, n), a;
	if (r != null) {
		if (typeof r == "number") {
			if (Number.isNaN(r)) return;
			a = r;
		} else a = typeof r == "boolean" ? r : typeof r == "string" ? i.spaceSeparated ? Ga(r) : i.commaSeparated ? Ha(r) : i.commaOrSpaceSeparated ? Ga(Ha(r).join(" ")) : Xa(i, i.property, r) : Array.isArray(r) ? [...r] : i.property === "style" ? Za(r) : String(r);
		if (Array.isArray(a)) {
			let e = [];
			for (let t of a) e.push(Xa(i, i.property, t));
			a = e;
		}
		i.property === "className" && Array.isArray(t.className) && (a = t.className.concat(a)), t[i.property] = a;
	}
}
function Ya(e, t) {
	if (t != null) {
		if (typeof t == "number" || typeof t == "string") e.push({
			type: "text",
			value: String(t)
		});
		else if (Array.isArray(t)) for (let n of t) Ya(e, n);
		else if (typeof t == "object" && "type" in t) t.type === "root" ? Ya(e, t.children) : e.push(t);
		else throw Error("Expected node, nodes, or string, got `" + t + "`");
	}
}
function Xa(e, t, n) {
	if (typeof n == "string") {
		if (e.number && n && !Number.isNaN(Number(n))) return Number(n);
		if ((e.boolean || e.overloadedBoolean) && (n === "" || pa(n) === pa(t))) return !0;
	}
	return n;
}
function Za(e) {
	let t = [];
	for (let [n, r] of Object.entries(e)) t.push([n, r].join(": "));
	return t.join("; ");
}
function Qa(e) {
	let t = /* @__PURE__ */ new Map();
	for (let n of e) t.set(n.toLowerCase(), n);
	return t;
}
//#endregion
//#region node_modules/hastscript/lib/svg-case-sensitive-tag-names.js
var $a = /* @__PURE__ */ "altGlyph.altGlyphDef.altGlyphItem.animateColor.animateMotion.animateTransform.clipPath.feBlend.feColorMatrix.feComponentTransfer.feComposite.feConvolveMatrix.feDiffuseLighting.feDisplacementMap.feDistantLight.feDropShadow.feFlood.feFuncA.feFuncB.feFuncG.feFuncR.feGaussianBlur.feImage.feMerge.feMergeNode.feMorphology.feOffset.fePointLight.feSpecularLighting.feSpotLight.feTile.feTurbulence.foreignObject.glyphRef.linearGradient.radialGradient.solidColor.textArea.textPath".split("."), eo = Ka(Ba, "div");
Ka(Va, "g", $a);
//#endregion
//#region node_modules/character-entities-legacy/index.js
var to = /* @__PURE__ */ "AElig.AMP.Aacute.Acirc.Agrave.Aring.Atilde.Auml.COPY.Ccedil.ETH.Eacute.Ecirc.Egrave.Euml.GT.Iacute.Icirc.Igrave.Iuml.LT.Ntilde.Oacute.Ocirc.Ograve.Oslash.Otilde.Ouml.QUOT.REG.THORN.Uacute.Ucirc.Ugrave.Uuml.Yacute.aacute.acirc.acute.aelig.agrave.amp.aring.atilde.auml.brvbar.ccedil.cedil.cent.copy.curren.deg.divide.eacute.ecirc.egrave.eth.euml.frac12.frac14.frac34.gt.iacute.icirc.iexcl.igrave.iquest.iuml.laquo.lt.macr.micro.middot.nbsp.not.ntilde.oacute.ocirc.ograve.ordf.ordm.oslash.otilde.ouml.para.plusmn.pound.quot.raquo.reg.sect.shy.sup1.sup2.sup3.szlig.thorn.times.uacute.ucirc.ugrave.uml.uuml.yacute.yen.yuml".split("."), no = {
	0: "�",
	128: "€",
	130: "‚",
	131: "ƒ",
	132: "„",
	133: "…",
	134: "†",
	135: "‡",
	136: "ˆ",
	137: "‰",
	138: "Š",
	139: "‹",
	140: "Œ",
	142: "Ž",
	145: "‘",
	146: "’",
	147: "“",
	148: "”",
	149: "•",
	150: "–",
	151: "—",
	152: "˜",
	153: "™",
	154: "š",
	155: "›",
	156: "œ",
	158: "ž",
	159: "Ÿ"
};
//#endregion
//#region node_modules/is-decimal/index.js
function ro(e) {
	let t = typeof e == "string" ? e.charCodeAt(0) : e;
	return t >= 48 && t <= 57;
}
//#endregion
//#region node_modules/is-hexadecimal/index.js
function io(e) {
	let t = typeof e == "string" ? e.charCodeAt(0) : e;
	return t >= 97 && t <= 102 || t >= 65 && t <= 70 || t >= 48 && t <= 57;
}
//#endregion
//#region node_modules/is-alphabetical/index.js
function ao(e) {
	let t = typeof e == "string" ? e.charCodeAt(0) : e;
	return t >= 97 && t <= 122 || t >= 65 && t <= 90;
}
//#endregion
//#region node_modules/is-alphanumerical/index.js
function oo(e) {
	return ao(e) || ro(e);
}
//#endregion
//#region node_modules/decode-named-character-reference/index.dom.js
var so = document.createElement("i");
function co(e) {
	let t = "&" + e + ";";
	so.innerHTML = t;
	let n = so.textContent;
	return n.charCodeAt(n.length - 1) === 59 && e !== "semi" ? !1 : n !== t && n;
}
//#endregion
//#region node_modules/parse-entities/lib/index.js
var W = [
	"",
	"Named character references must be terminated by a semicolon",
	"Numeric character references must be terminated by a semicolon",
	"Named character references cannot be empty",
	"Numeric character references cannot be empty",
	"Named character references must be known",
	"Numeric character references cannot be disallowed",
	"Numeric character references cannot be outside the permissible Unicode range"
];
function G(e, t) {
	let n = t || {}, r = typeof n.additional == "string" ? n.additional.charCodeAt(0) : n.additional, i = [], a = 0, o = -1, s = "", c, l;
	n.position && ("start" in n.position || "indent" in n.position ? (l = n.position.indent, c = n.position.start) : c = n.position);
	let u = (c ? c.line : 0) || 1, d = (c ? c.column : 0) || 1, f = m(), p;
	for (a--; ++a <= e.length;) if (p === 10 && (d = (l ? l[o] : 0) || 1), p = e.charCodeAt(a), p === 38) {
		let t = e.charCodeAt(a + 1);
		if (t === 9 || t === 10 || t === 12 || t === 32 || t === 38 || t === 60 || Number.isNaN(t) || r && t === r) {
			s += String.fromCharCode(p), d++;
			continue;
		}
		let o = a + 1, c = o, l = o, u;
		if (t === 35) {
			l = ++c;
			let t = e.charCodeAt(l);
			t === 88 || t === 120 ? (u = "hexadecimal", l = ++c) : u = "decimal";
		} else u = "named";
		let _ = "", v = "", y = "", b = u === "named" ? oo : u === "decimal" ? ro : io;
		for (l--; ++l <= e.length;) {
			let t = e.charCodeAt(l);
			if (!b(t)) break;
			y += String.fromCharCode(t), u === "named" && to.includes(y) && (_ = y, v = co(y));
		}
		let x = e.charCodeAt(l) === 59;
		if (x) {
			l++;
			let e = u === "named" && co(y);
			e && (_ = y, v = e);
		}
		let S = 1 + l - o, C = "";
		if (!(!x && n.nonTerminated === !1)) {
			if (!y) u !== "named" && h(4, S);
			else if (u === "named") {
				if (x && !v) h(5, 1);
				else if (_ !== y && (l = c + _.length, S = 1 + l - c, x = !1), !x) {
					let t = _ ? 1 : 3;
					if (n.attribute) {
						let n = e.charCodeAt(l);
						n === 61 ? (h(t, S), v = "") : oo(n) ? v = "" : h(t, S);
					} else h(t, S);
				}
				C = v;
			} else {
				x || h(2, S);
				let e = Number.parseInt(y, u === "hexadecimal" ? 16 : 10);
				if (lo(e)) h(7, S), C = "�";
				else if (e in no) h(6, S), C = no[e];
				else {
					let t = "";
					uo(e) && h(6, S), e > 65535 && (e -= 65536, t += String.fromCharCode(e >>> 10 | 55296), e = 56320 | e & 1023), C = t + String.fromCharCode(e);
				}
			}
		}
		if (C) {
			g(), f = m(), a = l - 1, d += l - o + 1, i.push(C);
			let t = m();
			t.offset++, n.reference && n.reference.call(n.referenceContext || void 0, C, {
				start: f,
				end: t
			}, e.slice(o - 1, l)), f = t;
		} else y = e.slice(o - 1, l), s += y, d += y.length, a = l - 1;
	} else p === 10 && (u++, o++, d = 0), Number.isNaN(p) ? g() : (s += String.fromCharCode(p), d++);
	return i.join("");
	function m() {
		return {
			line: u,
			column: d,
			offset: a + ((c ? c.offset : 0) || 0)
		};
	}
	function h(e, t) {
		let r;
		n.warning && (r = m(), r.column += t, r.offset += t, n.warning.call(n.warningContext || void 0, W[e], r, e));
	}
	function g() {
		s &&= (i.push(s), n.text && n.text.call(n.textContext || void 0, s, {
			start: f,
			end: m()
		}), "");
	}
}
function lo(e) {
	return e >= 55296 && e <= 57343 || e > 1114111;
}
function uo(e) {
	return e >= 1 && e <= 8 || e === 11 || e >= 13 && e <= 31 || e >= 127 && e <= 159 || e >= 64976 && e <= 65007 || (e & 65535) == 65535 || (e & 65535) == 65534;
}
//#endregion
//#region node_modules/refractor/lib/prism-core.js
var fo = 0, po = {}, mo = {
	util: {
		type: function(e) {
			return Object.prototype.toString.call(e).slice(8, -1);
		},
		objId: function(e) {
			return e.__id || Object.defineProperty(e, "__id", { value: ++fo }), e.__id;
		},
		clone: function e(t, n) {
			n ||= {};
			var r, i;
			switch (mo.util.type(t)) {
				case "Object":
					if (i = mo.util.objId(t), n[i]) return n[i];
					for (var a in r = {}, n[i] = r, t) t.hasOwnProperty(a) && (r[a] = e(t[a], n));
					return r;
				case "Array": return i = mo.util.objId(t), n[i] ? n[i] : (r = [], n[i] = r, t.forEach(function(t, i) {
					r[i] = e(t, n);
				}), r);
				default: return t;
			}
		}
	},
	languages: {
		plain: po,
		plaintext: po,
		text: po,
		txt: po,
		extend: function(e, t) {
			var n = mo.util.clone(mo.languages[e]);
			for (var r in t) n[r] = t[r];
			return n;
		},
		insertBefore: function(e, t, n, r) {
			r ||= mo.languages;
			var i = r[e], a = {};
			for (var o in i) if (i.hasOwnProperty(o)) {
				if (o == t) for (var s in n) n.hasOwnProperty(s) && (a[s] = n[s]);
				n.hasOwnProperty(o) || (a[o] = i[o]);
			}
			var c = r[e];
			return r[e] = a, mo.languages.DFS(mo.languages, function(t, n) {
				n === c && t != e && (this[t] = a);
			}), a;
		},
		DFS: function e(t, n, r, i) {
			i ||= {};
			var a = mo.util.objId;
			for (var o in t) if (t.hasOwnProperty(o)) {
				n.call(t, o, t[o], r || o);
				var s = t[o], c = mo.util.type(s);
				c === "Object" && !i[a(s)] ? (i[a(s)] = !0, e(s, n, null, i)) : c === "Array" && !i[a(s)] && (i[a(s)] = !0, e(s, n, o, i));
			}
		}
	},
	plugins: {},
	highlight: function(e, t, n) {
		var r = {
			code: e,
			grammar: t,
			language: n
		};
		if (mo.hooks.run("before-tokenize", r), !r.grammar) throw Error("The language \"" + r.language + "\" has no grammar.");
		return r.tokens = mo.tokenize(r.code, r.grammar), mo.hooks.run("after-tokenize", r), ho.stringify(mo.util.encode(r.tokens), r.language);
	},
	tokenize: function(e, t) {
		var n = t.rest;
		if (n) {
			for (var r in n) t[r] = n[r];
			delete t.rest;
		}
		var i = new vo();
		return yo(i, i.head, e), _o(e, i, t, i.head, 0), xo(i);
	},
	hooks: {
		all: {},
		add: function(e, t) {
			var n = mo.hooks.all;
			n[e] = n[e] || [], n[e].push(t);
		},
		run: function(e, t) {
			var n = mo.hooks.all[e];
			if (!(!n || !n.length)) for (var r = 0, i; i = n[r++];) i(t);
		}
	},
	Token: ho
};
function ho(e, t, n, r) {
	this.type = e, this.content = t, this.alias = n, this.length = (r || "").length | 0;
}
function go(e, t, n, r) {
	e.lastIndex = t;
	var i = e.exec(n);
	if (i && r && i[1]) {
		var a = i[1].length;
		i.index += a, i[0] = i[0].slice(a);
	}
	return i;
}
function _o(e, t, n, r, i, a) {
	for (var o in n) if (!(!n.hasOwnProperty(o) || !n[o])) {
		var s = n[o];
		s = Array.isArray(s) ? s : [s];
		for (var c = 0; c < s.length; ++c) {
			if (a && a.cause == o + "," + c) return;
			var l = s[c], u = l.inside, d = !!l.lookbehind, f = !!l.greedy, p = l.alias;
			if (f && !l.pattern.global) {
				var m = l.pattern.toString().match(/[imsuy]*$/)[0];
				l.pattern = RegExp(l.pattern.source, m + "g");
			}
			for (var h = l.pattern || l, g = r.next, _ = i; g !== t.tail && !(a && _ >= a.reach); _ += g.value.length, g = g.next) {
				var v = g.value;
				if (t.length > e.length) return;
				if (!(v instanceof ho)) {
					var y = 1, b;
					if (f) {
						if (b = go(h, _, e, d), !b || b.index >= e.length) break;
						var x = b.index, S = b.index + b[0].length, C = _;
						for (C += g.value.length; x >= C;) g = g.next, C += g.value.length;
						if (C -= g.value.length, _ = C, g.value instanceof ho) continue;
						for (var w = g; w !== t.tail && (C < S || typeof w.value == "string"); w = w.next) y++, C += w.value.length;
						y--, v = e.slice(_, C), b.index -= _;
					} else if (b = go(h, 0, v, d), !b) continue;
					var x = b.index, T = b[0], E = v.slice(0, x), D = v.slice(x + T.length), O = _ + v.length;
					a && O > a.reach && (a.reach = O);
					var k = g.prev;
					E && (k = yo(t, k, E), _ += E.length), bo(t, k, y);
					var A = new ho(o, u ? mo.tokenize(T, u) : T, p, T);
					if (g = yo(t, k, A), D && yo(t, g, D), y > 1) {
						var j = {
							cause: o + "," + c,
							reach: O
						};
						_o(e, t, n, g.prev, _, j), a && j.reach > a.reach && (a.reach = j.reach);
					}
				}
			}
		}
	}
}
function vo() {
	var e = {
		value: null,
		prev: null,
		next: null
	}, t = {
		value: null,
		prev: e,
		next: null
	};
	e.next = t, this.head = e, this.tail = t, this.length = 0;
}
function yo(e, t, n) {
	var r = t.next, i = {
		value: n,
		prev: t,
		next: r
	};
	return t.next = i, r.prev = i, e.length++, i;
}
function bo(e, t, n) {
	for (var r = t.next, i = 0; i < n && r !== e.tail; i++) r = r.next;
	t.next = r, r.prev = t, e.length -= i;
}
function xo(e) {
	for (var t = [], n = e.head.next; n !== e.tail;) t.push(n.value), n = n.next;
	return t;
}
var So = mo;
//#endregion
//#region node_modules/refractor/lib/core.js
function Co() {}
Co.prototype = So;
var wo = new Co();
wo.highlight = To, wo.register = Eo, wo.alias = Do, wo.registered = Oo, wo.listLanguages = ko, wo.util.encode = jo, wo.Token.stringify = Ao;
function To(e, t) {
	if (typeof e != "string") throw TypeError("Expected `string` for `value`, got `" + e + "`");
	let n, r;
	/* c8 ignore next 2 */
	if (t && typeof t == "object") n = t;
	else {
		if (r = t, typeof r != "string") throw TypeError("Expected `string` for `name`, got `" + r + "`");
		if (Object.hasOwn(wo.languages, r)) n = wo.languages[r];
		else throw Error("Unknown language: `" + r + "` is not registered");
	}
	return {
		type: "root",
		children: So.highlight.call(wo, e, n, r)
	};
}
function Eo(e) {
	if (typeof e != "function" || !e.displayName) throw Error("Expected `function` for `syntax`, got `" + e + "`");
	Object.hasOwn(wo.languages, e.displayName) || e(wo);
}
function Do(e, t) {
	let n = wo.languages, r = {};
	typeof e == "string" ? t && (r[e] = t) : r = e;
	let i;
	for (i in r) if (Object.hasOwn(r, i)) {
		let e = r[i], t = typeof e == "string" ? [e] : e, a = -1;
		for (; ++a < t.length;) n[t[a]] = n[i];
	}
}
function Oo(e) {
	if (typeof e != "string") throw TypeError("Expected `string` for `aliasOrLanguage`, got `" + e + "`");
	return Object.hasOwn(wo.languages, e);
}
function ko() {
	let e = wo.languages, t = [], n;
	for (n in e) Object.hasOwn(e, n) && typeof e[n] == "object" && t.push(n);
	return t;
}
function Ao(e, t) {
	if (typeof e == "string") return {
		type: "text",
		value: e
	};
	if (Array.isArray(e)) {
		let n = [], r = -1;
		for (; ++r < e.length;) e[r] !== null && e[r] !== void 0 && e[r] !== "" && n.push(Ao(e[r], t));
		return n;
	}
	let n = {
		attributes: {},
		classes: ["token", e.type],
		content: Ao(e.content, t),
		language: t,
		tag: "span",
		type: e.type
	};
	return e.alias && n.classes.push(...typeof e.alias == "string" ? [e.alias] : e.alias), wo.hooks.run("wrap", n), eo(n.tag + "." + n.classes.join("."), Mo(n.attributes), n.content);
}
function jo(e) {
	return e;
}
function Mo(e) {
	let t;
	for (t in e) Object.hasOwn(e, t) && (e[t] = G(e[t]));
	return e;
}
No.displayName = "clike", No.aliases = [];
function No(e) {
	e.languages.clike = {
		comment: [{
			pattern: /(^|[^\\])\/\*[\s\S]*?(?:\*\/|$)/,
			lookbehind: !0,
			greedy: !0
		}, {
			pattern: /(^|[^\\:])\/\/.*/,
			lookbehind: !0,
			greedy: !0
		}],
		string: {
			pattern: /(["'])(?:\\(?:\r\n|[\s\S])|(?!\1)[^\\\r\n])*\1/,
			greedy: !0
		},
		"class-name": {
			pattern: /(\b(?:class|extends|implements|instanceof|interface|new|trait)\s+|\bcatch\s+\()[\w.\\]+/i,
			lookbehind: !0,
			inside: { punctuation: /[.\\]/ }
		},
		keyword: /\b(?:break|catch|continue|do|else|finally|for|function|if|in|instanceof|new|null|return|throw|try|while)\b/,
		boolean: /\b(?:false|true)\b/,
		function: /\b\w+(?=\()/,
		number: /\b0x[\da-f]+\b|(?:\b\d+(?:\.\d*)?|\B\.\d+)(?:e[+-]?\d+)?/i,
		operator: /[<>]=?|[!=]=?=?|--?|\+\+?|&&?|\|\|?|[?*/~^%]/,
		punctuation: /[{}[\];(),.:]/
	};
}
Po.displayName = "csharp", Po.aliases = ["cs", "dotnet"];
function Po(e) {
	e.register(No), (function(e) {
		function t(e, t) {
			return e.replace(/<<(\d+)>>/g, function(e, n) {
				return "(?:" + t[+n] + ")";
			});
		}
		function n(e, n, r) {
			return RegExp(t(e, n), r || "");
		}
		function r(e, t) {
			for (var n = 0; n < t; n++) e = e.replace(/<<self>>/g, function() {
				return "(?:" + e + ")";
			});
			return e.replace(/<<self>>/g, "[^\\s\\S]");
		}
		var i = {
			type: "bool byte char decimal double dynamic float int long object sbyte short string uint ulong ushort var void",
			typeDeclaration: "class enum interface record struct",
			contextual: "add alias and ascending async await by descending from(?=\\s*(?:\\w|$)) get global group into init(?=\\s*;) join let nameof not notnull on or orderby partial remove select set unmanaged value when where with(?=\\s*{)",
			other: "abstract as base break case catch checked const continue default delegate do else event explicit extern finally fixed for foreach goto if implicit in internal is lock namespace new null operator out override params private protected public readonly ref return sealed sizeof stackalloc static switch this throw try typeof unchecked unsafe using virtual volatile while yield"
		};
		function a(e) {
			return "\\b(?:" + e.trim().replace(/ /g, "|") + ")\\b";
		}
		var o = a(i.typeDeclaration), s = RegExp(a(i.type + " " + i.typeDeclaration + " " + i.contextual + " " + i.other)), c = a(i.typeDeclaration + " " + i.contextual + " " + i.other), l = a(i.type + " " + i.typeDeclaration + " " + i.other), u = r("<(?:[^<>;=+\\-*/%&|^]|<<self>>)*>", 2), d = r("\\((?:[^()]|<<self>>)*\\)", 2), f = "@?\\b[A-Za-z_]\\w*\\b", p = t("<<0>>(?:\\s*<<1>>)?", [f, u]), m = t("(?!<<0>>)<<1>>(?:\\s*\\.\\s*<<1>>)*", [c, p]), h = "\\[\\s*(?:,\\s*)*\\]", g = t("<<0>>(?:\\s*(?:\\?\\s*)?<<1>>)*(?:\\s*\\?)?", [m, h]), _ = t("(?:<<0>>|<<1>>)(?:\\s*(?:\\?\\s*)?<<2>>)*(?:\\s*\\?)?", [
			t("\\(<<0>>+(?:,<<0>>+)+\\)", [t("[^,()<>[\\];=+\\-*/%&|^]|<<0>>|<<1>>|<<2>>", [
				u,
				d,
				h
			])]),
			m,
			h
		]), v = {
			keyword: s,
			punctuation: /[<>()?,.:[\]]/
		}, y = "'(?:[^\\r\\n'\\\\]|\\\\.|\\\\[Uux][\\da-fA-F]{1,8})'", b = "\"(?:\\\\.|[^\\\\\"\\r\\n])*\"", x = "@\"(?:\"\"|\\\\[\\s\\S]|[^\\\\\"])*\"(?!\")";
		e.languages.csharp = e.languages.extend("clike", {
			string: [{
				pattern: n("(^|[^$\\\\])<<0>>", [x]),
				lookbehind: !0,
				greedy: !0
			}, {
				pattern: n("(^|[^@$\\\\])<<0>>", [b]),
				lookbehind: !0,
				greedy: !0
			}],
			"class-name": [
				{
					pattern: n("(\\busing\\s+static\\s+)<<0>>(?=\\s*;)", [m]),
					lookbehind: !0,
					inside: v
				},
				{
					pattern: n("(\\busing\\s+<<0>>\\s*=\\s*)<<1>>(?=\\s*;)", [f, _]),
					lookbehind: !0,
					inside: v
				},
				{
					pattern: n("(\\busing\\s+)<<0>>(?=\\s*=)", [f]),
					lookbehind: !0
				},
				{
					pattern: n("(\\b<<0>>\\s+)<<1>>", [o, p]),
					lookbehind: !0,
					inside: v
				},
				{
					pattern: n("(\\bcatch\\s*\\(\\s*)<<0>>", [m]),
					lookbehind: !0,
					inside: v
				},
				{
					pattern: n("(\\bwhere\\s+)<<0>>", [f]),
					lookbehind: !0
				},
				{
					pattern: n("(\\b(?:is(?:\\s+not)?|as)\\s+)<<0>>", [g]),
					lookbehind: !0,
					inside: v
				},
				{
					pattern: n("\\b<<0>>(?=\\s+(?!<<1>>|with\\s*\\{)<<2>>(?:\\s*[=,;:{)\\]]|\\s+(?:in|when)\\b))", [
						_,
						l,
						f
					]),
					inside: v
				}
			],
			keyword: s,
			number: /(?:\b0(?:x[\da-f_]*[\da-f]|b[01_]*[01])|(?:\B\.\d+(?:_+\d+)*|\b\d+(?:_+\d+)*(?:\.\d+(?:_+\d+)*)?)(?:e[-+]?\d+(?:_+\d+)*)?)(?:[dflmu]|lu|ul)?\b/i,
			operator: />>=?|<<=?|[-=]>|([-+&|])\1|~|\?\?=?|[-+*/%&|^!=<>]=?/,
			punctuation: /\?\.?|::|[{}[\];(),.:]/
		}), e.languages.insertBefore("csharp", "number", { range: {
			pattern: /\.\./,
			alias: "operator"
		} }), e.languages.insertBefore("csharp", "punctuation", { "named-parameter": {
			pattern: n("([(,]\\s*)<<0>>(?=\\s*:)", [f]),
			lookbehind: !0,
			alias: "punctuation"
		} }), e.languages.insertBefore("csharp", "class-name", {
			namespace: {
				pattern: n("(\\b(?:namespace|using)\\s+)<<0>>(?:\\s*\\.\\s*<<0>>)*(?=\\s*[;{])", [f]),
				lookbehind: !0,
				inside: { punctuation: /\./ }
			},
			"type-expression": {
				pattern: n("(\\b(?:default|sizeof|typeof)\\s*\\(\\s*(?!\\s))(?:[^()\\s]|\\s(?!\\s)|<<0>>)*(?=\\s*\\))", [d]),
				lookbehind: !0,
				alias: "class-name",
				inside: v
			},
			"return-type": {
				pattern: n("<<0>>(?=\\s+(?:<<1>>\\s*(?:=>|[({]|\\.\\s*this\\s*\\[)|this\\s*\\[))", [_, m]),
				inside: v,
				alias: "class-name"
			},
			"constructor-invocation": {
				pattern: n("(\\bnew\\s+)<<0>>(?=\\s*[[({])", [_]),
				lookbehind: !0,
				inside: v,
				alias: "class-name"
			},
			"generic-method": {
				pattern: n("<<0>>\\s*<<1>>(?=\\s*\\()", [f, u]),
				inside: {
					function: n("^<<0>>", [f]),
					generic: {
						pattern: RegExp(u),
						alias: "class-name",
						inside: v
					}
				}
			},
			"type-list": {
				pattern: n("\\b((?:<<0>>\\s+<<1>>|record\\s+<<1>>\\s*<<5>>|where\\s+<<2>>)\\s*:\\s*)(?:<<3>>|<<4>>|<<1>>\\s*<<5>>|<<6>>)(?:\\s*,\\s*(?:<<3>>|<<4>>|<<6>>))*(?=\\s*(?:where|[{;]|=>|$))", [
					o,
					p,
					f,
					_,
					s.source,
					d,
					"\\bnew\\s*\\(\\s*\\)"
				]),
				lookbehind: !0,
				inside: {
					"record-arguments": {
						pattern: n("(^(?!new\\s*\\()<<0>>\\s*)<<1>>", [p, d]),
						lookbehind: !0,
						greedy: !0,
						inside: e.languages.csharp
					},
					keyword: s,
					"class-name": {
						pattern: RegExp(_),
						greedy: !0,
						inside: v
					},
					punctuation: /[,()]/
				}
			},
			preprocessor: {
				pattern: /(^[\t ]*)#.*/m,
				lookbehind: !0,
				alias: "property",
				inside: { directive: {
					pattern: /(#)\b(?:define|elif|else|endif|endregion|error|if|line|nullable|pragma|region|undef|warning)\b/,
					lookbehind: !0,
					alias: "keyword"
				} }
			}
		});
		var S = b + "|" + y, C = t("\\/(?![*/])|\\/\\/[^\\r\\n]*[\\r\\n]|\\/\\*(?:[^*]|\\*(?!\\/))*\\*\\/|<<0>>", [S]), w = r(t("[^\"'/()]|<<0>>|\\(<<self>>*\\)", [C]), 2), T = "\\b(?:assembly|event|field|method|module|param|property|return|type)\\b", E = t("<<0>>(?:\\s*\\(<<1>>*\\))?", [m, w]);
		e.languages.insertBefore("csharp", "class-name", { attribute: {
			pattern: n("((?:^|[^\\s\\w>)?])\\s*\\[\\s*)(?:<<0>>\\s*:\\s*)?<<1>>(?:\\s*,\\s*<<1>>)*(?=\\s*\\])", [T, E]),
			lookbehind: !0,
			greedy: !0,
			inside: {
				target: {
					pattern: n("^<<0>>(?=\\s*:)", [T]),
					alias: "keyword"
				},
				"attribute-arguments": {
					pattern: n("\\(<<0>>*\\)", [w]),
					inside: e.languages.csharp
				},
				"class-name": {
					pattern: RegExp(m),
					inside: { punctuation: /\./ }
				},
				punctuation: /[:,]/
			}
		} });
		var D = ":[^}\\r\\n]+", O = r(t("[^\"'/()]|<<0>>|\\(<<self>>*\\)", [C]), 2), k = t("\\{(?!\\{)(?:(?![}:])<<0>>)*<<1>>?\\}", [O, D]), A = r(t("[^\"'/()]|\\/(?!\\*)|\\/\\*(?:[^*]|\\*(?!\\/))*\\*\\/|<<0>>|\\(<<self>>*\\)", [S]), 2), j = t("\\{(?!\\{)(?:(?![}:])<<0>>)*<<1>>?\\}", [A, D]);
		function M(t, r) {
			return {
				interpolation: {
					pattern: n("((?:^|[^{])(?:\\{\\{)*)<<0>>", [t]),
					lookbehind: !0,
					inside: {
						"format-string": {
							pattern: n("(^\\{(?:(?![}:])<<0>>)*)<<1>>(?=\\}$)", [r, D]),
							lookbehind: !0,
							inside: { punctuation: /^:/ }
						},
						punctuation: /^\{|\}$/,
						expression: {
							pattern: /[\s\S]+/,
							alias: "language-csharp",
							inside: e.languages.csharp
						}
					}
				},
				string: /[\s\S]+/
			};
		}
		e.languages.insertBefore("csharp", "string", {
			"interpolation-string": [{
				pattern: n("(^|[^\\\\])(?:\\$@|@\\$)\"(?:\"\"|\\\\[\\s\\S]|\\{\\{|<<0>>|[^\\\\{\"])*\"", [k]),
				lookbehind: !0,
				greedy: !0,
				inside: M(k, O)
			}, {
				pattern: n("(^|[^@\\\\])\\$\"(?:\\\\.|\\{\\{|<<0>>|[^\\\\\"{])*\"", [j]),
				lookbehind: !0,
				greedy: !0,
				inside: M(j, A)
			}],
			char: {
				pattern: RegExp(y),
				greedy: !0
			}
		}), e.languages.dotnet = e.languages.cs = e.languages.csharp;
	})(e);
}
//#endregion
//#region src/effect-delta.ts
var Fo = () => ({
	baseById: /* @__PURE__ */ new Map(),
	headById: /* @__PURE__ */ new Map(),
	baseByLine: /* @__PURE__ */ new Map(),
	headByLine: /* @__PURE__ */ new Map()
});
function Io(e, t) {
	return e.nearestDepth === t.nearestDepth && e.viaDispatchOnly === t.viaDispatchOnly && e.looped === t.looped;
}
function Lo(e, t, n) {
	if (!e || e.kind === "same") return "same";
	if (e.kind === "added") return t === "new" ? "added" : "same";
	if (e.kind === "removed") return t === "old" ? "removed" : "same";
	let r = t === "old" ? e.base : e.head;
	return r && Io(r, n) ? "changed" : "same";
}
function Ro(e, t) {
	let n = new Map(e.map((e) => [e.family, e])), r = new Map(t.map((e) => [e.family, e])), i = [.../* @__PURE__ */ new Set([...n.keys(), ...r.keys()])].sort();
	return new Map(i.map((e) => {
		let t = n.get(e), i = r.get(e);
		return [e, {
			kind: t ? i ? Io(t, i) ? "same" : "changed" : "removed" : "added",
			base: t,
			head: i
		}];
	}));
}
function zo(e) {
	if (!e.id || e.name === "" || e.name.startsWith(".")) return null;
	let t = e.id.indexOf("("), n = t < 0 ? e.id : e.id.slice(0, t), r = n.lastIndexOf(".");
	if (r < 0) return null;
	let i = n.slice(0, r), a = t < 0 ? "" : e.id.slice(t);
	return `${i}|${e.signature ? e.signature.replace(e.name, "<method>") : ""}|${a}`;
}
function Bo(e, t, n) {
	let r = e.get(t) || [];
	r.push(n), e.set(t, r);
}
function Vo(e, t, n) {
	if (!n) return Fo();
	let r = new Map(e.map((e) => [e.id, e])), i = new Map(t.map((e) => [e.id, e])), a = [];
	for (let t of e) {
		let e = i.get(t.id);
		e && (a.push([t, e]), r.delete(t.id), i.delete(e.id));
	}
	let o = (e) => {
		let t = /* @__PURE__ */ new Map();
		for (let n of e) {
			let e = zo(n);
			if (!e) continue;
			let r = t.get(e) || [];
			r.push(n), t.set(e, r);
		}
		return t;
	}, s = o(r.values()), c = o(i.values());
	for (let [e, t] of s) {
		let n = c.get(e);
		t.length === 1 && n?.length === 1 && (a.push([t[0], n[0]]), r.delete(t[0].id), i.delete(n[0].id));
	}
	let l = o(r.values()), u = o(i.values());
	for (let e of r.values()) {
		let t = zo(e);
		t && u.has(t) || a.push([e, void 0]);
	}
	for (let e of i.values()) {
		let t = zo(e);
		t && l.has(t) || a.push([void 0, e]);
	}
	let d = /* @__PURE__ */ new Map(), f = /* @__PURE__ */ new Map(), p = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map();
	for (let [e, t] of a) {
		let n = {
			base: e,
			head: t,
			effects: Ro(e?.effects || [], t?.effects || [])
		};
		e && (d.set(e.id, n), Bo(p, e.line, n)), t && (f.set(t.id, n), Bo(m, t.line, n));
	}
	return {
		baseById: d,
		headById: f,
		baseByLine: p,
		headByLine: m
	};
}
function Ho(e) {
	return [...e.effects.values()].filter((e) => e.kind !== "same");
}
//#endregion
//#region src/review-gutter.ts
function Uo(e, t, n, r = !1) {
	return e === "split" ? t === "normal" && r && n === "old" ? null : n : n === "old" ? null : t === "delete" ? "old" : "new";
}
//#endregion
//#region src/review-source.ts
function Wo(e) {
	return JSON.stringify([
		e.file,
		e.base.store,
		e.base.commit,
		e.head.store,
		e.head.commit
	]);
}
function Go(e) {
	if (!e.length) return null;
	let t = e.split("\n");
	return t.at(-1) === "" && t.pop(), {
		content: "",
		oldStart: 1,
		newStart: 1,
		oldLines: t.length,
		newLines: t.length,
		changes: t.map((n, r) => ({
			type: "normal",
			isNormal: !0,
			oldLineNumber: r + 1,
			newLineNumber: r + 1,
			content: n.endsWith("\r") && (r < t.length - 1 || e.endsWith("\n")) ? n.slice(0, -1) : n
		}))
	};
}
function Ko(e, t) {
	return e.length <= 2e5 && t <= 5e3;
}
function qo(e, t, n) {
	return e.file === t.file && e.side === n && e.store === t[n].store && e.commit === t[n].commit;
}
//#endregion
//#region src/review-presentation.ts
var Jo = [
	"inline",
	"gutter",
	"off"
], Yo = "rig.review.effectMode", Xo = () => window.localStorage;
function Zo(e = Xo) {
	try {
		let t = e().getItem(Yo);
		return Jo.includes(t) ? t : "inline";
	} catch {
		return "inline";
	}
}
function Qo(e, t = Xo) {
	try {
		t().setItem(Yo, e);
	} catch {}
}
var $o = {
	db: "Database",
	cache: "Cache",
	blob: "Object store",
	bus: "Message bus",
	echo: "Event channel",
	io: "File system / I/O",
	rpc: "Remote call",
	search: "Search"
};
function es(e) {
	return $o[e] || e;
}
function ts(e) {
	return [
		es(e.family),
		e.nearestDepth === 0 ? "direct" : `depth ${e.nearestDepth}`,
		e.viaDispatchOnly ? "possible dispatch" : "",
		e.looped ? "inside iteration" : ""
	].filter(Boolean).join(" · ");
}
function ns(e) {
	return `${e.provider}:${e.operation} · inside iteration`;
}
function rs(e) {
	return `${e.witnessProvider}:${e.witnessOperation} · reached from iterating call · depth ${e.witnessDepth} · candidate`;
}
function is(e) {
	if (e.semanticState === "not-present") return {
		state: "not-present",
		label: "file absent",
		detail: "This revision has no file to analyze."
	};
	if (e.semanticState === "not-indexed") return {
		state: "not-indexed",
		label: "not indexed",
		detail: "Source is available, but semantic findings are not."
	};
	if (e.findings === void 0) return {
		state: "loading",
		label: "findings loading…",
		detail: "Effects are independent; findings are still being loaded."
	};
	if (e.findings === null) return {
		state: "unavailable",
		label: "findings unavailable",
		detail: "Findings could not be loaded. This does not mean zero findings."
	};
	let { hazards: t, amplifications: n, anchors: r, crossMethodAvailable: i } = e.findings, a = t.length + n.length + r.length;
	return {
		state: i ? "ready" : "partial",
		label: `${a === 0 ? "no" : a} findings${i ? "" : " · cross-method off"}`,
		detail: i ? "Local and cross-method findings loaded. Iteration findings are candidates, not proof of runtime N+1." : "Local findings loaded. Cross-method analysis (tier 3) is disabled for this store; absence of anchors is not a negative result."
	};
}
function as(e, t) {
	return `Show effects for ${e === "old" ? "base" : "head"} line ${t}`;
}
function os(e, t) {
	if (!e || !t) return e === t;
	let n = ({ line: e, ...t }) => t, r = (e) => JSON.stringify({
		sites: e.sites.map(n),
		effects: e.effects,
		hazards: e.hazards.map(n),
		amplifications: e.amplifications.map(n),
		anchors: e.anchors.map(n)
	});
	return r(e) === r(t);
}
function ss(e, t, n) {
	return e && t === 0 && n === 0;
}
//#endregion
//#region src/file-diff.tsx
var cs = [
	{
		key: "db",
		mark: "D",
		label: "database"
	},
	{
		key: "cache",
		mark: "C",
		label: "cache"
	},
	{
		key: "blob",
		mark: "B",
		label: "blob/object store"
	},
	{
		key: "bus",
		mark: "Q",
		label: "message bus"
	},
	{
		key: "echo",
		mark: "E",
		label: "echo/event channel"
	},
	{
		key: "io",
		mark: "I",
		label: "file system / I/O"
	},
	{
		key: "rpc",
		mark: "R",
		label: "remote call"
	},
	{
		key: "search",
		mark: "S",
		label: "search"
	}
], ls = /* @__PURE__ */ new WeakMap();
wo.register(Po);
var us = { highlight(e, t) {
	return wo.highlight(e, t).children;
} };
function ds(e) {
	return e.slice(0, 12);
}
function fs(e) {
	let t = e.replaceAll("\\", "/"), n = t.lastIndexOf("/");
	return {
		name: n < 0 ? t : t.slice(n + 1),
		parent: n < 0 ? "" : t.slice(0, n)
	};
}
function ps(e) {
	if (!e) return "external effect";
	let t = e.replace(/^[A-Z]:/, "").split("(", 1)[0];
	return (t.split(/[.:+]/).pop() || t).replace(/``\d+$/, "<T>");
}
function ms(e) {
	let t = e.nearestDepth === 0 ? "!" : `:${e.nearestDepth}`;
	return `${e.family}${t}${e.looped ? "*" : ""}${e.viaDispatchOnly ? "?" : ""}`;
}
function hs(e) {
	return [
		`${ms(e)} — ${e.nearestDepth === 0 ? "the effect is in this call's body" : `nearest is ${e.nearestDepth} calls below`}`,
		e.viaDispatchOnly ? "BASIS: virtual/interface dispatch only — a lead, not a proven call" : "BASIS: a real call edge",
		e.looped ? "ITERATION: an effectful edge occurs inside an iteration; runtime count is not established" : ""
	].filter(Boolean).join("\n");
}
function gs(e, t) {
	return t === "old" ? e.type === "insert" ? null : e.type === "delete" ? e.lineNumber : e.oldLineNumber : e.type === "delete" ? null : e.type === "insert" ? e.lineNumber : e.newLineNumber;
}
function _s(e, t) {
	let n = e.find((e) => e.family === t.family);
	if (!n) {
		e.push({ ...t });
		return;
	}
	let r = n.viaDispatchOnly && !t.viaDispatchOnly, i = n.viaDispatchOnly === t.viaDispatchOnly && t.nearestDepth < n.nearestDepth, a = n.looped || t.looped;
	r || i ? Object.assign(n, t, { looped: a }) : n.looped = a;
}
function vs(e) {
	let t = /* @__PURE__ */ new Map(), n = (e) => {
		let n = t.get(e);
		return n || (n = {
			sites: [],
			effects: [],
			hazards: [],
			amplifications: [],
			anchors: []
		}, t.set(e, n)), n;
	};
	for (let t of e.effects?.sites || []) {
		let e = n(t.line);
		e.sites.push(t);
		for (let n of t.effects) _s(e.effects, n);
	}
	for (let t of e.findings?.hazards || []) n(t.line).hazards.push(t);
	for (let t of e.findings?.amplifications || []) n(t.line).amplifications.push(t);
	for (let t of e.findings?.anchors || []) n(t.line).anchors.push(t);
	for (let e of t.values()) e.effects.sort((e, t) => Number(e.viaDispatchOnly) - Number(t.viaDispatchOnly) || e.nearestDepth - t.nearestDepth || e.family.localeCompare(t.family));
	return t;
}
function ys(e, t) {
	return os(e, t);
}
function bs(e, t) {
	return e === "changed" ? "changed" : t === "old" && e === "removed" ? "removed" : t === "new" && e === "added" ? "added" : "same";
}
function xs(e, t, n, r, i) {
	let a = n.map((t) => bs(t.effects.get(e.family)?.kind, r)), o = (t?.sites || []).map((e) => (r === "old" ? i.baseById : i.headById).get(e.enclosingMethodId)).filter((e) => !!e).map((t) => Lo(t.effects.get(e.family), r, e)), s = [...a, ...o];
	return s.includes("changed") ? "changed" : s.includes("added") ? "added" : s.includes("removed") ? "removed" : "same";
}
function Ss(e, t) {
	let n = e.flatMap((e) => Ho(e).map((e) => {
		let n = es((t === "old" ? e.base : e.head)?.family || e.base?.family || e.head?.family || "effect");
		if (e.kind === "added") return `+${n}`;
		if (e.kind === "removed") return `−${n}`;
		let r = [
			e.base?.nearestDepth === e.head?.nearestDepth ? "" : "distance",
			e.base?.looped === e.head?.looped ? "" : "repetition",
			e.base?.viaDispatchOnly === e.head?.viaDispatchOnly ? "" : "dispatch basis"
		].filter(Boolean).join(", ");
		return `△${n}${r ? ` (${r})` : ""}`;
	}));
	return n.length ? `Method reach changed: ${n.join(" · ")}` : "";
}
function Cs({ insight: e, headers: t, side: n, deltas: r }) {
	let i = [];
	for (let t of e?.effects || []) _s(i, t);
	for (let e of t) {
		let t = n === "old" ? e.base : e.head;
		for (let e of t?.effects || []) _s(i, e);
	}
	let a = new Map(i.map((e) => [e.family, e])), o = i.length + (e?.hazards.length || 0) + (e?.amplifications.length || 0) + (e?.anchors.length || 0);
	return /* @__PURE__ */ (0, m.jsxs)("span", {
		className: "rig-diff-marks",
		"aria-label": `${o} semantic annotations`,
		children: [/* @__PURE__ */ (0, m.jsxs)("span", {
			className: "rig-diff-finding-stack",
			children: [
				e?.hazards.length ? /* @__PURE__ */ (0, m.jsx)("span", {
					className: "rig-diff-finding hazard",
					title: `${e.hazards.length} tier-1 hazard(s)`,
					children: "⚠"
				}) : null,
				e?.anchors.length ? /* @__PURE__ */ (0, m.jsx)("span", {
					className: "rig-diff-finding anchor",
					title: `${e.anchors.length} cross-method amplification anchor(s)`,
					children: "↓"
				}) : null,
				e?.amplifications.length ? /* @__PURE__ */ (0, m.jsx)("span", {
					className: "rig-diff-finding amplification",
					title: `${e.amplifications.length} looped effect(s)`,
					children: "⟳"
				}) : null
			]
		}), /* @__PURE__ */ (0, m.jsx)("span", {
			className: "rig-diff-lane",
			"aria-label": "effect reach lane",
			children: cs.map((i) => {
				let o = a.get(i.key), s = o ? xs(o, e, t, n, r) : "same", c = o ? `${i.label}: ${hs(o)}${s === "same" ? "" : `\nDELTA: ${s} at method grain`}` : i.label;
				return /* @__PURE__ */ (0, m.jsx)("span", {
					className: [
						"rig-diff-slot",
						o ? "on" : "off",
						o?.nearestDepth === 0 ? "here" : "below",
						o?.viaDispatchOnly ? "uncertain" : "",
						o?.looped ? "amp" : "",
						s === "same" ? "" : `moved ${s}`
					].filter(Boolean).join(" "),
					"data-family": i.key,
					title: c
				}, i.key);
			})
		})]
	});
}
function ws({ expanded: e, insight: t, headers: n = [], callbacks: r, deltas: i }) {
	return /* @__PURE__ */ (0, m.jsxs)("div", {
		className: "rig-diff-widget",
		"data-rig-side": e.side,
		"data-rig-line": e.line,
		children: [
			/* @__PURE__ */ (0, m.jsxs)("strong", { children: [
				e.side === "old" ? "base" : "head",
				":",
				e.line
			] }),
			n.map((t, n) => /* @__PURE__ */ (0, m.jsxs)("span", {
				className: "rig-diff-method-summary",
				children: [
					(e.side === "old" ? t.base : t.head)?.name,
					": ",
					Ss([t], e.side)
				]
			}, n)),
			/* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-findings",
				children: [
					t?.hazards.map((e, t) => /* @__PURE__ */ (0, m.jsxs)("span", {
						className: `rig-diff-finding-row hazard confidence-${e.confidence}`,
						children: [
							/* @__PURE__ */ (0, m.jsx)("strong", { children: e.type.replaceAll("_", " ") }),
							" · ",
							e.confidence,
							" · ",
							e.subtype,
							e.detail ? /* @__PURE__ */ (0, m.jsx)("span", {
								className: "rig-diff-finding-detail",
								children: e.detail
							}) : null
						]
					}, `hazard:${e.type}:${t}`)),
					t?.amplifications.map((e, t) => /* @__PURE__ */ (0, m.jsxs)("span", {
						className: "rig-diff-finding-row amplification",
						children: [ns(e), /* @__PURE__ */ (0, m.jsxs)("span", {
							className: "rig-diff-finding-detail",
							children: [
								"Iteration: ",
								e.iteration,
								" · ",
								e.confidence,
								" confidence"
							]
						})]
					}, `amplification:${e.provider}:${t}`)),
					t?.anchors.map((e, t) => /* @__PURE__ */ (0, m.jsxs)("span", {
						className: `rig-diff-finding-row anchor confidence-${e.confidence}`,
						children: [rs(e), /* @__PURE__ */ (0, m.jsxs)("span", {
							className: "rig-diff-finding-detail",
							children: [
								e.caller,
								" · ",
								e.iterationKind,
								" · ",
								e.confidence,
								" confidence",
								e.witnessResource ? ` · ${e.witnessResource}` : ""
							]
						})]
					}, `anchor:${e.witnessProvider}:${t}`))
				]
			}),
			t && (t.anchors.length > 0 || t.amplifications.length > 0) ? /* @__PURE__ */ (0, m.jsx)("span", {
				className: "rig-diff-candidate-note",
				children: "Static iteration candidate — not proof of runtime N+1 or a query count."
			}) : null,
			t?.sites.map((t, n) => {
				let a = t.targetMethodId || t.enclosingMethodId;
				return /* @__PURE__ */ (0, m.jsxs)("button", {
					type: "button",
					className: "rig-diff-path",
					onClick: () => r.onOpenTree?.(a, e.side),
					disabled: !a || !r.onOpenTree,
					title: a || "No symbol identity for this external effect",
					children: [
						/* @__PURE__ */ (0, m.jsx)("span", { children: ps(t.targetMethodId) }),
						t.effects.map((n, r) => {
							let a = Lo((e.side === "old" ? i.baseById : i.headById).get(t.enclosingMethodId)?.effects.get(n.family), e.side, n);
							return /* @__PURE__ */ (0, m.jsxs)("span", {
								className: a === "same" ? "rig-diff-effect-detail" : `rig-diff-effect-detail rig-diff-inline-delta ${a}`,
								title: `${hs(n)}${a === "same" ? "" : `\n${a} at method grain`}`,
								children: [a === "same" ? "" : a === "added" ? "+ " : a === "removed" ? "− " : "△ ", ts(n)]
							}, `${n.family}:${r}`);
						}),
						/* @__PURE__ */ (0, m.jsx)("span", {
							className: "rig-diff-open",
							children: "open tree ↗"
						})
					]
				}, `${a}:${n}`);
			})
		]
	});
}
function Ts({ model: e, callbacks: t }) {
	let n = (0, h.useRef)(null), [r, i] = (0, h.useState)("unified"), [a, o] = (0, h.useState)(!0), [s, c] = (0, h.useState)(() => Zo()), [l, u] = (0, h.useState)(null), d = Wo(e), [f, p] = (0, h.useState)(null), g = f?.identity === d, _ = f?.side || "head", v = _ === "base" ? "old" : "new", [y, b] = (0, h.useState)(null), [x, S] = (0, h.useState)(0), C = JSON.stringify([
		d,
		_,
		x
	]), w = g && y?.key === C ? y.value : void 0, T = g && y?.key === C ? y.error : void 0, E = (0, h.useRef)(t.onLoadSource);
	E.current = t.onLoadSource, (0, h.useEffect)(() => {
		p(null), b(null);
	}, [d]), (0, h.useEffect)(() => {
		if (!g) return;
		let t = !1;
		b(null);
		let n = E.current;
		if (n) return n(_).then((n) => {
			if (!t) {
				if (!qo(n, e, _)) throw Error("Source revision does not match the selected review.");
				b({
					key: C,
					value: n
				});
			}
		}).catch((e) => {
			t || b({
				key: C,
				error: e instanceof Error ? e.message : "Source request failed."
			});
		}), () => {
			t = !0;
		};
	}, [
		d,
		g,
		_,
		x
	]);
	let D = (0, h.useMemo)(() => w?.state === "available" && w.content !== null ? Go(w.content) : null, [w]), O = g ? "unified" : r, k = (0, h.useMemo)(() => ({}), [
		e.file,
		e.base.store,
		e.head.store,
		e.base.commit,
		e.head.commit,
		e.patch,
		g,
		_
	]), A = l?.context === k ? l : null, [j, M] = (0, h.useState)(null), ee = (0, h.useMemo)(() => e.patch.trim() ? oe(e.patch) : [], [e.patch]), te = (0, h.useMemo)(() => vs(e.base), [e.base]), ne = (0, h.useMemo)(() => vs(e.head), [e.head]), N = ee[0], P = (0, h.useMemo)(() => g ? D ? [D] : [] : N?.hunks || [], [
		g,
		D,
		N
	]), re = !g || Ko(w?.content || "", D?.newLines || 0), ie = g ? w?.language : e.language, ae = g && e[_].path || e.relativePath, F = (0, h.useMemo)(() => fs(ae || e.file), [ae, e.file]), I = (0, h.useMemo)(() => N ? N.hunks.reduce((e, t) => {
		for (let n of t.changes) n.type === "insert" ? e.additions += 1 : n.type === "delete" && (e.deletions += 1);
		return e;
	}, {
		additions: 0,
		deletions: 0
	}) : {
		additions: 0,
		deletions: 0
	}, [N]), L = e.base.semanticState === "available" && e.base.effects !== null, se = e.head.semanticState === "available" && e.head.effects !== null, ce = (0, h.useMemo)(() => Vo(e.base.effects?.methods || [], e.head.effects?.methods || [], L && se), [
		e.base.effects,
		e.head.effects,
		L,
		se
	]), le = e.base.effects?.sites.length || 0, ue = e.head.effects?.sites.length || 0, de = is(e.base), fe = is(e.head), pe = ue - le, me = L && se ? `effect sites ${pe > 0 ? "+" : ""}${pe}` : L ? "base-only semantics" : se ? "head-only semantics" : e.language === "text" ? "text-only · semantics unavailable" : "semantics unavailable", he = (0, h.useMemo)(() => P.length && ie === "csharp" && re ? ua(P, {
		highlight: !0,
		refractor: us,
		language: "csharp",
		enhancers: g ? [] : [ca(P)]
	}) : null, [
		P,
		ie,
		g,
		re
	]), ge = (0, h.useMemo)(() => {
		let e = /* @__PURE__ */ new Map(), t = (e) => new Map([...e].map(([e, t]) => [e, t.filter((e) => Ho(e).length > 0)])), n = t(ce.baseByLine), r = t(ce.headByLine);
		for (let t of P) for (let i of t.changes) {
			let t = (e) => {
				if (g && e !== v) return;
				let t = gs(i, e);
				if (t == null) return;
				let a = (e === "old" ? te : ne).get(t), o = (e === "old" ? n : r).get(t) || [];
				return a || o.length ? {
					side: e,
					line: t,
					insight: a,
					headers: o
				} : void 0;
			}, a = t("old"), o = t("new"), s = ys(a?.insight, o?.insight) && (a?.headers.length || 0) === (o?.headers.length || 0) && (a?.headers || []).every((e, t) => e === o?.headers[t]);
			e.set(Er(i), {
				change: i,
				old: a,
				new: o,
				identical: s
			});
		}
		return e;
	}, [
		P,
		te,
		ne,
		ce,
		g,
		v
	]), _e = (e) => u((t) => t?.context === k && t.key === e.key && t.side === e.side ? null : {
		...e,
		context: k
	}), ve = (0, h.useMemo)(() => {
		let e = {};
		if (s === "off" || !A) return e;
		let n = ge.get(A.key), r = n?.[A.side];
		if (!n || !r) return e;
		let i = /* @__PURE__ */ (0, m.jsx)(ws, {
			expanded: A,
			insight: r.insight,
			headers: r.headers,
			callbacks: t,
			deltas: ce
		});
		return e[A.key] = O === "split" && n.change.type === "normal" ? /* @__PURE__ */ (0, m.jsxs)("div", {
			className: "rig-diff-inline-pair",
			children: [/* @__PURE__ */ (0, m.jsx)("div", { children: A.side === "old" ? i : null }), /* @__PURE__ */ (0, m.jsx)("div", { children: A.side === "new" ? i : null })]
		}) : /* @__PURE__ */ (0, m.jsx)("div", {
			className: "rig-diff-inline-single",
			children: i
		}), e;
	}, [
		ge,
		s,
		A,
		O,
		t,
		k,
		ce
	]);
	return (0, h.useEffect)(() => u(null), [k]), (0, h.useEffect)(() => {
		let e = t.focusLine;
		if (!e) {
			M(null);
			return;
		}
		let r = requestAnimationFrame(() => {
			let t = n.current?.querySelector(`.rig-diff-gutter[data-rig-side="${e.side}"][data-rig-line="${e.line}"]`);
			M(!!t), t?.closest("tr")?.scrollIntoView({ block: "center" });
		});
		return () => cancelAnimationFrame(r);
	}, [
		t.focusLine?.line,
		t.focusLine?.side,
		e.patch,
		O,
		D
	]), /* @__PURE__ */ (0, m.jsxs)("div", {
		className: `rig-diff-island view-${O} effects-${s} ${g ? "full-source" : "patch-source"} ${a ? "wrap-lines" : "no-wrap"}`,
		ref: n,
		children: [
			/* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-head",
				children: [/* @__PURE__ */ (0, m.jsxs)("div", {
					className: "rig-diff-identity",
					title: ae,
					children: [
						/* @__PURE__ */ (0, m.jsxs)("div", {
							className: "rig-diff-file-line",
							children: [
								/* @__PURE__ */ (0, m.jsx)("span", {
									className: `rig-diff-status status-${e.status.toLowerCase()}`,
									title: `Git status ${e.status}`,
									children: e.status
								}),
								/* @__PURE__ */ (0, m.jsx)("strong", { children: F.name }),
								g ? /* @__PURE__ */ (0, m.jsxs)("span", {
									className: "rig-source-revision",
									children: [
										_ === "base" ? "Base" : "Head",
										" · ",
										ds(e[_].commit)
									]
								}) : /* @__PURE__ */ (0, m.jsxs)("span", {
									className: "rig-diff-patch-counts",
									"aria-label": `${I.additions} additions, ${I.deletions} deletions`,
									children: [/* @__PURE__ */ (0, m.jsxs)("b", { children: ["+", I.additions] }), /* @__PURE__ */ (0, m.jsxs)("i", { children: ["−", I.deletions] })]
								})
							]
						}),
						F.parent ? /* @__PURE__ */ (0, m.jsx)("span", {
							className: "rig-diff-parent",
							children: F.parent
						}) : null,
						e.oldPath && e.newPath && e.oldPath !== e.newPath ? /* @__PURE__ */ (0, m.jsxs)("span", {
							className: "rig-diff-path-change",
							children: [
								e.oldPath,
								" → ",
								e.newPath
							]
						}) : e.oldPath && !e.newPath ? /* @__PURE__ */ (0, m.jsxs)("span", {
							className: "rig-diff-path-change",
							children: ["deleted from ", e.oldPath]
						}) : !e.oldPath && e.newPath ? /* @__PURE__ */ (0, m.jsxs)("span", {
							className: "rig-diff-path-change",
							children: ["added as ", e.newPath]
						}) : null,
						/* @__PURE__ */ (0, m.jsxs)("span", {
							className: "rig-diff-revisions",
							children: [
								ds(e.base.commit),
								" → ",
								ds(e.head.commit)
							]
						})
					]
				}), /* @__PURE__ */ (0, m.jsxs)("div", {
					className: "rig-diff-summary",
					children: [
						t.onLoadSource ? /* @__PURE__ */ (0, m.jsx)("button", {
							type: "button",
							className: "rig-diff-toolbar-button",
							"aria-pressed": g,
							onClick: () => {
								p(g ? null : {
									identity: d,
									side: e.newPath ? "head" : "base"
								});
							},
							children: g ? "Back to diff" : "Full file"
						}) : null,
						g ? /* @__PURE__ */ (0, m.jsxs)("select", {
							className: "rig-diff-toolbar-button",
							"aria-label": "File revision",
							value: _,
							onChange: (e) => p({
								identity: d,
								side: e.target.value
							}),
							children: [/* @__PURE__ */ (0, m.jsx)("option", {
								value: "base",
								children: "Base"
							}), /* @__PURE__ */ (0, m.jsx)("option", {
								value: "head",
								children: "Head"
							})]
						}) : null,
						t.onFilesHiddenChange ? /* @__PURE__ */ (0, m.jsx)("button", {
							type: "button",
							className: "rig-diff-toolbar-button",
							"aria-expanded": !t.filesHidden,
							onClick: () => t.onFilesHiddenChange?.(!t.filesHidden),
							children: t.filesHidden ? "Show files" : "Hide files"
						}) : null,
						t.onFocusModeChange ? /* @__PURE__ */ (0, m.jsx)("button", {
							type: "button",
							className: "rig-diff-toolbar-button",
							"aria-pressed": !!t.focusMode,
							title: "Use the full app viewport for review. Escape exits focus mode.",
							onClick: () => t.onFocusModeChange?.(!t.focusMode),
							children: t.focusMode ? "Exit focus" : "Focus mode"
						}) : null,
						/* @__PURE__ */ (0, m.jsx)("span", {
							className: `rig-diff-effect-delta ${pe > 0 ? "added" : pe < 0 ? "removed" : "stable"}`,
							title: L && se ? `${le} base effect sites → ${ue} head effect sites` : `base: ${e.base.semanticState}; head: ${e.head.semanticState}`,
							children: me
						}),
						/* @__PURE__ */ (0, m.jsxs)("label", {
							className: "rig-diff-viewed",
							title: "Mark this file as reviewed (V)",
							children: [/* @__PURE__ */ (0, m.jsx)("input", {
								type: "checkbox",
								checked: t.viewed || !1,
								onChange: (e) => t.onViewedChange?.(e.target.checked)
							}), "Viewed"]
						}),
						/* @__PURE__ */ (0, m.jsxs)("details", {
							className: "rig-diff-settings",
							children: [/* @__PURE__ */ (0, m.jsx)("summary", {
								"aria-label": "Diff settings",
								title: "Diff settings",
								children: "⚙"
							}), /* @__PURE__ */ (0, m.jsxs)("div", {
								className: "rig-diff-settings-menu",
								children: [
									/* @__PURE__ */ (0, m.jsxs)("fieldset", { children: [/* @__PURE__ */ (0, m.jsx)("legend", { children: "Effect annotations" }), Jo.map((e) => /* @__PURE__ */ (0, m.jsxs)("label", { children: [/* @__PURE__ */ (0, m.jsx)("input", {
										type: "radio",
										name: "rig-effect-display",
										value: e,
										checked: s === e,
										onChange: () => {
											c(e), Qo(e), u(null);
										}
									}), e === "inline" ? "Inline" : e === "gutter" ? "Gutter" : "Off"] }, e))] }),
									/* @__PURE__ */ (0, m.jsxs)("fieldset", {
										disabled: g,
										children: [
											/* @__PURE__ */ (0, m.jsx)("legend", { children: "Diff display" }),
											/* @__PURE__ */ (0, m.jsxs)("label", { children: [/* @__PURE__ */ (0, m.jsx)("input", {
												type: "radio",
												name: "rig-diff-display",
												value: "unified",
												checked: r === "unified",
												onChange: () => i("unified")
											}), "Unified"] }),
											/* @__PURE__ */ (0, m.jsxs)("label", { children: [/* @__PURE__ */ (0, m.jsx)("input", {
												type: "radio",
												name: "rig-diff-display",
												value: "split",
												checked: r === "split",
												onChange: () => i("split")
											}), "Split"] })
										]
									}),
									/* @__PURE__ */ (0, m.jsxs)("label", {
										className: "rig-diff-settings-check",
										children: [/* @__PURE__ */ (0, m.jsx)("input", {
											type: "checkbox",
											disabled: g,
											checked: t.ignoreWhitespace || !1,
											onChange: (e) => t.onIgnoreWhitespaceChange?.(e.target.checked)
										}), "Hide whitespace changes"]
									}),
									/* @__PURE__ */ (0, m.jsxs)("label", {
										className: "rig-diff-settings-check",
										children: [/* @__PURE__ */ (0, m.jsx)("input", {
											type: "checkbox",
											checked: a,
											onChange: (e) => o(e.target.checked)
										}), "Wrap long lines"]
									})
								]
							})]
						})
					]
				})]
			}),
			/* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-readiness",
				"aria-live": "polite",
				children: [
					/* @__PURE__ */ (0, m.jsxs)("span", { children: ["Effects: ", s === "off" ? "hidden" : s] }),
					/* @__PURE__ */ (0, m.jsxs)("span", {
						"data-findings-side": "base",
						"data-state": de.state,
						title: de.detail,
						children: ["Base: ", de.label]
					}),
					/* @__PURE__ */ (0, m.jsxs)("span", {
						"data-findings-side": "head",
						"data-state": fe.state,
						title: fe.detail,
						children: ["Head: ", fe.label]
					})
				]
			}),
			s === "gutter" && (L || se) ? /* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-lane-key",
				title: "Effect reach by family",
				children: [/* @__PURE__ */ (0, m.jsx)("span", {
					className: "rig-diff-lanehead",
					"aria-label": "Effect lane columns",
					children: cs.map((e) => /* @__PURE__ */ (0, m.jsx)("b", {
						title: e.label,
						children: e.mark
					}, e.key))
				}), /* @__PURE__ */ (0, m.jsxs)("details", {
					className: "rig-diff-lane-help",
					children: [/* @__PURE__ */ (0, m.jsx)("summary", {
						"aria-label": "Explain effect reach lane",
						title: "Explain effect reach lane",
						children: "?"
					}), /* @__PURE__ */ (0, m.jsxs)("div", { children: [
						/* @__PURE__ */ (0, m.jsx)("strong", { children: "Effect reach" }),
						/* @__PURE__ */ (0, m.jsx)("span", { children: "● in this call · ○ through callees" }),
						/* @__PURE__ */ (0, m.jsx)("span", { children: "teal changed · violet edge repeated" }),
						/* @__PURE__ */ (0, m.jsx)("span", { children: "exact depth and dispatch basis are in each mark's tooltip" })
					] })]
				})]
			}) : null,
			t.focusLine && j === !1 && !g ? /* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-focus-note",
				children: [
					t.focusLine.side === "old" ? "Base" : "Head",
					" line ",
					t.focusLine.line,
					" is outside the changed hunks and their ",
					e.contextLines,
					"-line context."
				]
			}) : null,
			g && (!w || w.state !== "available" || !D) ? /* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-empty rig-source-message",
				role: "status",
				children: [T || (w ? w.reason || "Empty file — 0 bytes in this revision." : "Loading exact file from Git…"), T || w?.state === "unavailable" ? /* @__PURE__ */ (0, m.jsx)("button", {
					type: "button",
					className: "rig-diff-toolbar-button",
					onClick: () => S((e) => e + 1),
					children: "Retry source"
				}) : null]
			}) : !g && !N ? /* @__PURE__ */ (0, m.jsx)("div", {
				className: "rig-diff-empty",
				children: "No textual changes in this file."
			}) : /* @__PURE__ */ (0, m.jsxs)(m.Fragment, { children: [g && !re ? /* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-focus-note",
				children: [
					"Full file · ",
					D?.newLines,
					" lines. Syntax highlighting is off for this large file; all source lines remain available."
				]
			}) : null, /* @__PURE__ */ (0, m.jsx)(fi, {
				viewType: O,
				diffType: g ? "modify" : N.type,
				hunks: P,
				tokens: he,
				widgets: ve,
				renderGutter: ({ change: e, side: n, renderDefault: i, wrapInAnchor: a }) => {
					let o = gs(e, n), c = Er(e), l = ge.get(c), u = ss(l?.identical || !1, l?.old?.headers.length || 0, l?.new?.headers.length || 0), d = s === "off" ? null : g ? n === "new" ? v : null : Uo(r, e.type, n, u), f = d == null ? null : gs(e, d), p = d == null ? void 0 : l?.[d], h = p?.insight, _ = p?.headers || [], y = g ? v : n, b = t.focusLine?.side === y && t.focusLine.line === o, x = _.length > 0 || (h?.effects || []).some((e) => xs(e, h, _, d, ce) !== "same"), S = !!h?.hazards.length, C = !!(h?.amplifications.length || h?.anchors.length), w = s === "gutter" && (h || _.length) ? /* @__PURE__ */ (0, m.jsx)(Cs, {
						insight: h,
						headers: _,
						side: d,
						deltas: ce
					}) : null, T = [
						d == null ? "" : Ss(_, d),
						...(h?.effects || []).map(ts),
						S ? "Hazard findings — click for details" : "",
						C ? "Iteration candidate — not proof of runtime N+1" : ""
					].filter(Boolean).join("\n");
					return a(/* @__PURE__ */ (0, m.jsxs)("span", {
						className: `rig-diff-gutter${b ? " focus" : ""}${_.length ? " method-change" : ""}`,
						"data-rig-side": g && n === "old" ? void 0 : y,
						"data-rig-line": g && n === "old" ? void 0 : o ?? void 0,
						title: d == null ? void 0 : Ss(_, d) || void 0,
						children: [h || _.length ? /* @__PURE__ */ (0, m.jsx)("button", {
							type: "button",
							className: s === "inline" ? `rig-diff-disclosure-trigger${x ? " changed" : ""}${S ? " hazard" : ""}${C ? " amplification" : ""}` : "rig-diff-mark-button",
							title: T || "Show effects and open their call trees",
							"aria-label": as(d, f),
							"aria-expanded": A?.key === c && A.side === d,
							"data-rig-side": d ?? void 0,
							"data-rig-line": f ?? void 0,
							onClick: (e) => {
								e.preventDefault(), e.stopPropagation(), _e({
									key: c,
									side: d,
									line: f
								});
							},
							children: s === "inline" ? /* @__PURE__ */ (0, m.jsx)("svg", {
								viewBox: "0 0 16 20",
								width: "13",
								height: "16",
								"aria-hidden": "true",
								children: /* @__PURE__ */ (0, m.jsx)("path", {
									fill: "currentColor",
									d: "M9 1 2 11h5l-1 8L14 8H9z"
								})
							}) : w
						}) : w, i()]
					}));
				},
				children: (e) => e.map((e) => /* @__PURE__ */ (0, m.jsx)(si, { hunk: e }, e.content))
			})] })
		]
	});
}
function Es(e, t, n = {}) {
	let r = ls.get(e);
	r || (r = (0, p.createRoot)(e), ls.set(e, r)), r.render(/* @__PURE__ */ (0, m.jsx)(Ts, {
		model: t,
		callbacks: n
	}));
}
function Ds(e) {
	ls.get(e)?.unmount(), ls.delete(e);
}
//#endregion
export { Es as mountFileDiff, Ds as unmountFileDiff };
