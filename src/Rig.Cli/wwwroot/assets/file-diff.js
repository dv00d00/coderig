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
	function ee(e, t) {
		return E(e.type, t, e.props);
	}
	function D(e) {
		return typeof e == "object" && !!e && e.$$typeof === t;
	}
	function O(e) {
		var t = {
			"=": "=0",
			":": "=2"
		};
		return "$" + e.replace(/[=:]/g, function(e) {
			return t[e];
		});
	}
	var te = /\/+/g;
	function k(e, t) {
		return typeof e == "object" && e && e.key != null ? O("" + e.key) : t.toString(36);
	}
	function A(e) {
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
	function ne(e, r, i, a, o) {
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
				case d: return c = e._init, ne(c(e._payload), r, i, a, o);
			}
		}
		if (c) return o = o(e), c = a === "" ? "." + k(e, 0) : a, S(o) ? (i = "", c != null && (i = c.replace(te, "$&/") + "/"), ne(o, r, i, "", function(e) {
			return e;
		})) : o != null && (D(o) && (o = ee(o, i + (o.key == null || e && e.key === o.key ? "" : ("" + o.key).replace(te, "$&/") + "/") + c)), r.push(o)), 1;
		c = 0;
		var l = a === "" ? "." : a + ":";
		if (S(e)) for (var u = 0; u < e.length; u++) a = e[u], s = l + k(a, u), c += ne(a, r, i, s, o);
		else if (u = m(e), typeof u == "function") for (e = u.call(e), u = 0; !(a = e.next()).done;) a = a.value, s = l + k(a, u++), c += ne(a, r, i, s, o);
		else if (s === "object") {
			if (typeof e.then == "function") return ne(A(e), r, i, a, o);
			throw r = String(e), Error("Objects are not valid as a React child (found: " + (r === "[object Object]" ? "object with keys {" + Object.keys(e).join(", ") + "}" : r) + "). If you meant to render a collection of children, use an array instead.");
		}
		return c;
	}
	function re(e, t, n) {
		if (e == null) return e;
		var r = [], i = 0;
		return ne(e, r, "", "", function(e) {
			return t.call(n, e, i++);
		}), r;
	}
	function ie(e) {
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
	var j = typeof reportError == "function" ? reportError : function(e) {
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
	}, M = {
		map: re,
		forEach: function(e, t, n) {
			re(e, function() {
				t.apply(this, arguments);
			}, n);
		},
		count: function(e) {
			var t = 0;
			return re(e, function() {
				t++;
			}), t;
		},
		toArray: function(e) {
			return re(e, function(e) {
				return e;
			}) || [];
		},
		only: function(e) {
			if (!D(e)) throw Error("React.Children.only expected to receive a single React element child.");
			return e;
		}
	};
	e.Activity = f, e.Children = M, e.Component = v, e.Fragment = r, e.Profiler = a, e.PureComponent = b, e.StrictMode = i, e.Suspense = l, e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = w, e.__COMPILER_RUNTIME = {
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
	}, e.isValidElement = D, e.lazy = function(e) {
		return {
			$$typeof: d,
			_payload: {
				_status: -1,
				_result: e
			},
			_init: ie
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
			i !== null && i(n, r), typeof r == "object" && r && typeof r.then == "function" && r.then(C, j);
		} catch (e) {
			j(e);
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
			if (n(c) !== null) m = !0, S || (S = !0, D());
			else {
				var t = n(l);
				t !== null && k(x, t.startTime - e);
			}
		}
	}
	var S = !1, C = -1, w = 5, T = -1;
	function E() {
		return g ? !0 : !(e.unstable_now() - T < w);
	}
	function ee() {
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
								u !== null && k(x, u.startTime - t), i = !1;
							}
						}
						break a;
					} finally {
						d = null, f = a, p = !1;
					}
					i = void 0;
				}
			} finally {
				i ? D() : S = !1;
			}
		}
	}
	var D;
	if (typeof y == "function") D = function() {
		y(ee);
	};
	else if (typeof MessageChannel < "u") {
		var O = new MessageChannel(), te = O.port2;
		O.port1.onmessage = ee, D = function() {
			te.postMessage(null);
		};
	} else D = function() {
		_(ee, 0);
	};
	function k(t, n) {
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
		}, a > o ? (r.sortIndex = a, t(l, r), n(c) === null && r === n(l) && (h ? (v(C), C = -1) : h = !0, k(x, a - o))) : (r.sortIndex = s, t(c, r), m || p || (m = !0, S || (S = !0, D()))), r;
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
	var h = Object.assign, g = Symbol.for("react.element"), _ = Symbol.for("react.transitional.element"), v = Symbol.for("react.portal"), y = Symbol.for("react.fragment"), b = Symbol.for("react.strict_mode"), x = Symbol.for("react.profiler"), S = Symbol.for("react.consumer"), C = Symbol.for("react.context"), w = Symbol.for("react.forward_ref"), T = Symbol.for("react.suspense"), E = Symbol.for("react.suspense_list"), ee = Symbol.for("react.memo"), D = Symbol.for("react.lazy"), O = Symbol.for("react.activity"), te = Symbol.for("react.memo_cache_sentinel"), k = Symbol.iterator;
	function A(e) {
		return typeof e != "object" || !e ? null : (e = k && e[k] || e["@@iterator"], typeof e == "function" ? e : null);
	}
	var ne = Symbol.for("react.client.reference");
	function re(e) {
		if (e == null) return null;
		if (typeof e == "function") return e.$$typeof === ne ? null : e.displayName || e.name || null;
		if (typeof e == "string") return e;
		switch (e) {
			case y: return "Fragment";
			case x: return "Profiler";
			case b: return "StrictMode";
			case T: return "Suspense";
			case E: return "SuspenseList";
			case O: return "Activity";
		}
		if (typeof e == "object") switch (e.$$typeof) {
			case v: return "Portal";
			case C: return e.displayName || "Context";
			case S: return (e._context.displayName || "Context") + ".Consumer";
			case w:
				var t = e.render;
				return e = e.displayName, e ||= (e = t.displayName || t.name || "", e === "" ? "ForwardRef" : "ForwardRef(" + e + ")"), e;
			case ee: return t = e.displayName || null, t === null ? re(e.type) || "Memo" : t;
			case D:
				t = e._payload, e = e._init;
				try {
					return re(e(t));
				} catch {}
		}
		return null;
	}
	var ie = Array.isArray, j = n.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, M = r.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE, ae = {
		pending: !1,
		data: null,
		method: null,
		action: null
	}, oe = [], se = -1;
	function ce(e) {
		return { current: e };
	}
	function le(e) {
		0 > se || (e.current = oe[se], oe[se] = null, se--);
	}
	function N(e, t) {
		se++, oe[se] = e.current, e.current = t;
	}
	var ue = ce(null), de = ce(null), fe = ce(null), pe = ce(null);
	function me(e, t) {
		switch (N(fe, t), N(de, e), N(ue, null), t.nodeType) {
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
		le(ue), N(ue, e);
	}
	function he() {
		le(ue), le(de), le(fe);
	}
	function ge(e) {
		e.memoizedState !== null && N(pe, e);
		var t = ue.current, n = Hd(t, e.type);
		t !== n && (N(de, e), N(ue, n));
	}
	function _e(e) {
		de.current === e && (le(ue), le(de)), pe.current === e && (le(pe), Qf._currentValue = ae);
	}
	var ve, ye;
	function be(e) {
		if (ve === void 0) try {
			throw Error();
		} catch (e) {
			var t = e.stack.trim().match(/\n( *(at )?)/);
			ve = t && t[1] || "", ye = -1 < e.stack.indexOf("\n    at") ? " (<anonymous>)" : -1 < e.stack.indexOf("@") ? "@unknown:0:0" : "";
		}
		return "\n" + ve + e + ye;
	}
	var xe = !1;
	function Se(e, t) {
		if (!e || xe) return "";
		xe = !0;
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
			xe = !1, Error.prepareStackTrace = n;
		}
		return (n = e ? e.displayName || e.name : "") ? be(n) : "";
	}
	function Ce(e, t) {
		switch (e.tag) {
			case 26:
			case 27:
			case 5: return be(e.type);
			case 16: return be("Lazy");
			case 13: return e.child !== t && t !== null ? be("Suspense Fallback") : be("Suspense");
			case 19: return be("SuspenseList");
			case 0:
			case 15: return Se(e.type, !1);
			case 11: return Se(e.type.render, !1);
			case 1: return Se(e.type, !0);
			case 31: return be("Activity");
			default: return "";
		}
	}
	function we(e) {
		try {
			var t = "", n = null;
			do
				t += Ce(e, n), n = e, e = e.return;
			while (e);
			return t;
		} catch (e) {
			return "\nError generating stack: " + e.message + "\n" + e.stack;
		}
	}
	var Te = Object.prototype.hasOwnProperty, Ee = t.unstable_scheduleCallback, De = t.unstable_cancelCallback, Oe = t.unstable_shouldYield, ke = t.unstable_requestPaint, Ae = t.unstable_now, je = t.unstable_getCurrentPriorityLevel, Me = t.unstable_ImmediatePriority, Ne = t.unstable_UserBlockingPriority, Pe = t.unstable_NormalPriority, Fe = t.unstable_LowPriority, Ie = t.unstable_IdlePriority, Le = t.log, Re = t.unstable_setDisableYieldValue, ze = null, Be = null;
	function Ve(e) {
		if (typeof Le == "function" && Re(e), Be && typeof Be.setStrictMode == "function") try {
			Be.setStrictMode(ze, e);
		} catch {}
	}
	var He = Math.clz32 ? Math.clz32 : Ge, Ue = Math.log, We = Math.LN2;
	function Ge(e) {
		return e >>>= 0, e === 0 ? 32 : 31 - (Ue(e) / We | 0) | 0;
	}
	var Ke = 256, qe = 262144, Je = 4194304;
	function Ye(e) {
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
	function Xe(e, t, n) {
		var r = e.pendingLanes;
		if (r === 0) return 0;
		var i = 0, a = e.suspendedLanes, o = e.pingedLanes;
		e = e.warmLanes;
		var s = r & 134217727;
		return s === 0 ? (s = r & ~a, s === 0 ? o === 0 ? n || (n = r & ~e, n !== 0 && (i = Ye(n))) : i = Ye(o) : i = Ye(s)) : (r = s & ~a, r === 0 ? (o &= s, o === 0 ? n || (n = s & ~e, n !== 0 && (i = Ye(n))) : i = Ye(o)) : i = Ye(r)), i === 0 ? 0 : t !== 0 && t !== i && (t & a) === 0 && (a = i & -i, n = t & -t, a >= n || a === 32 && n & 4194048) ? t : i;
	}
	function Ze(e, t) {
		return (e.pendingLanes & ~(e.suspendedLanes & ~e.pingedLanes) & t) === 0;
	}
	function Qe(e, t) {
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
	function $e() {
		var e = Je;
		return Je <<= 1, !(Je & 62914560) && (Je = 4194304), e;
	}
	function et(e) {
		for (var t = [], n = 0; 31 > n; n++) t.push(e);
		return t;
	}
	function tt(e, t) {
		e.pendingLanes |= t, t !== 268435456 && (e.suspendedLanes = 0, e.pingedLanes = 0, e.warmLanes = 0);
	}
	function nt(e, t, n, r, i, a) {
		var o = e.pendingLanes;
		e.pendingLanes = n, e.suspendedLanes = 0, e.pingedLanes = 0, e.warmLanes = 0, e.expiredLanes &= n, e.entangledLanes &= n, e.errorRecoveryDisabledLanes &= n, e.shellSuspendCounter = 0;
		var s = e.entanglements, c = e.expirationTimes, l = e.hiddenUpdates;
		for (n = o & ~n; 0 < n;) {
			var u = 31 - He(n), d = 1 << u;
			s[u] = 0, c[u] = -1;
			var f = l[u];
			if (f !== null) for (l[u] = null, u = 0; u < f.length; u++) {
				var p = f[u];
				p !== null && (p.lane &= -536870913);
			}
			n &= ~d;
		}
		r !== 0 && rt(e, r, 0), a !== 0 && i === 0 && e.tag !== 0 && (e.suspendedLanes |= a & ~(o & ~t));
	}
	function rt(e, t, n) {
		e.pendingLanes |= t, e.suspendedLanes &= ~t;
		var r = 31 - He(t);
		e.entangledLanes |= t, e.entanglements[r] = e.entanglements[r] | 1073741824 | n & 261930;
	}
	function it(e, t) {
		var n = e.entangledLanes |= t;
		for (e = e.entanglements; n;) {
			var r = 31 - He(n), i = 1 << r;
			i & t | e[r] & t && (e[r] |= t), n &= ~i;
		}
	}
	function at(e, t) {
		var n = t & -t;
		return n = n & 42 ? 1 : ot(n), (n & (e.suspendedLanes | t)) === 0 ? n : 0;
	}
	function ot(e) {
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
	function st(e) {
		return e &= -e, 2 < e ? 8 < e ? e & 134217727 ? 32 : 268435456 : 8 : 2;
	}
	function ct() {
		var e = M.p;
		return e === 0 ? (e = window.event, e === void 0 ? 32 : mp(e.type)) : e;
	}
	function lt(e, t) {
		var n = M.p;
		try {
			return M.p = e, t();
		} finally {
			M.p = n;
		}
	}
	var ut = Math.random().toString(36).slice(2), dt = "__reactFiber$" + ut, ft = "__reactProps$" + ut, pt = "__reactContainer$" + ut, mt = "__reactEvents$" + ut, ht = "__reactListeners$" + ut, gt = "__reactHandles$" + ut, _t = "__reactResources$" + ut, vt = "__reactMarker$" + ut;
	function yt(e) {
		delete e[dt], delete e[ft], delete e[mt], delete e[ht], delete e[gt];
	}
	function bt(e) {
		var t = e[dt];
		if (t) return t;
		for (var n = e.parentNode; n;) {
			if (t = n[pt] || n[dt]) {
				if (n = t.alternate, t.child !== null || n !== null && n.child !== null) for (e = df(e); e !== null;) {
					if (n = e[dt]) return n;
					e = df(e);
				}
				return t;
			}
			e = n, n = e.parentNode;
		}
		return null;
	}
	function xt(e) {
		if (e = e[dt] || e[pt]) {
			var t = e.tag;
			if (t === 5 || t === 6 || t === 13 || t === 31 || t === 26 || t === 27 || t === 3) return e;
		}
		return null;
	}
	function St(e) {
		var t = e.tag;
		if (t === 5 || t === 26 || t === 27 || t === 6) return e.stateNode;
		throw Error(a(33));
	}
	function Ct(e) {
		var t = e[_t];
		return t ||= e[_t] = {
			hoistableStyles: /* @__PURE__ */ new Map(),
			hoistableScripts: /* @__PURE__ */ new Map()
		}, t;
	}
	function P(e) {
		e[vt] = !0;
	}
	var wt = /* @__PURE__ */ new Set(), Tt = {};
	function Et(e, t) {
		Dt(e, t), Dt(e + "Capture", t);
	}
	function Dt(e, t) {
		for (Tt[e] = t, e = 0; e < t.length; e++) wt.add(t[e]);
	}
	var Ot = RegExp("^[:A-Z_a-z\\u00C0-\\u00D6\\u00D8-\\u00F6\\u00F8-\\u02FF\\u0370-\\u037D\\u037F-\\u1FFF\\u200C-\\u200D\\u2070-\\u218F\\u2C00-\\u2FEF\\u3001-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFFD][:A-Z_a-z\\u00C0-\\u00D6\\u00D8-\\u00F6\\u00F8-\\u02FF\\u0370-\\u037D\\u037F-\\u1FFF\\u200C-\\u200D\\u2070-\\u218F\\u2C00-\\u2FEF\\u3001-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFFD\\-.0-9\\u00B7\\u0300-\\u036F\\u203F-\\u2040]*$"), kt = {}, At = {};
	function jt(e) {
		return Te.call(At, e) ? !0 : Te.call(kt, e) ? !1 : Ot.test(e) ? At[e] = !0 : (kt[e] = !0, !1);
	}
	function Mt(e, t, n) {
		if (jt(t)) {
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
	function Nt(e, t, n) {
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
	function Pt(e, t, n, r) {
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
	function Ft(e) {
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
	function It(e) {
		var t = e.type;
		return (e = e.nodeName) && e.toLowerCase() === "input" && (t === "checkbox" || t === "radio");
	}
	function Lt(e, t, n) {
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
	function Rt(e) {
		if (!e._valueTracker) {
			var t = It(e) ? "checked" : "value";
			e._valueTracker = Lt(e, t, "" + e[t]);
		}
	}
	function zt(e) {
		if (!e) return !1;
		var t = e._valueTracker;
		if (!t) return !0;
		var n = t.getValue(), r = "";
		return e && (r = It(e) ? e.checked ? "true" : "false" : e.value), e = r, e !== n && (t.setValue(e), !0);
	}
	function Bt(e) {
		if (e ||= typeof document < "u" ? document : void 0, e === void 0) return null;
		try {
			return e.activeElement || e.body;
		} catch {
			return e.body;
		}
	}
	var Vt = /[\n"\\]/g;
	function F(e) {
		return e.replace(Vt, function(e) {
			return "\\" + e.charCodeAt(0).toString(16) + " ";
		});
	}
	function Ht(e, t, n, r, i, a, o, s) {
		e.name = "", o != null && typeof o != "function" && typeof o != "symbol" && typeof o != "boolean" ? e.type = o : e.removeAttribute("type"), t == null ? o !== "submit" && o !== "reset" || e.removeAttribute("value") : o === "number" ? (t === 0 && e.value === "" || e.value != t) && (e.value = "" + Ft(t)) : e.value !== "" + Ft(t) && (e.value = "" + Ft(t)), t == null ? n == null ? r != null && e.removeAttribute("value") : Wt(e, o, Ft(n)) : Wt(e, o, Ft(t)), i == null && a != null && (e.defaultChecked = !!a), i != null && (e.checked = i && typeof i != "function" && typeof i != "symbol"), s != null && typeof s != "function" && typeof s != "symbol" && typeof s != "boolean" ? e.name = "" + Ft(s) : e.removeAttribute("name");
	}
	function Ut(e, t, n, r, i, a, o, s) {
		if (a != null && typeof a != "function" && typeof a != "symbol" && typeof a != "boolean" && (e.type = a), t != null || n != null) {
			if (!(a !== "submit" && a !== "reset" || t != null)) {
				Rt(e);
				return;
			}
			n = n == null ? "" : "" + Ft(n), t = t == null ? n : "" + Ft(t), s || t === e.value || (e.value = t), e.defaultValue = t;
		}
		r ??= i, r = typeof r != "function" && typeof r != "symbol" && !!r, e.checked = s ? e.checked : !!r, e.defaultChecked = !!r, o != null && typeof o != "function" && typeof o != "symbol" && typeof o != "boolean" && (e.name = o), Rt(e);
	}
	function Wt(e, t, n) {
		t === "number" && Bt(e.ownerDocument) === e || e.defaultValue === "" + n || (e.defaultValue = "" + n);
	}
	function Gt(e, t, n, r) {
		if (e = e.options, t) {
			t = {};
			for (var i = 0; i < n.length; i++) t["$" + n[i]] = !0;
			for (n = 0; n < e.length; n++) i = t.hasOwnProperty("$" + e[n].value), e[n].selected !== i && (e[n].selected = i), i && r && (e[n].defaultSelected = !0);
		} else {
			for (n = "" + Ft(n), t = null, i = 0; i < e.length; i++) {
				if (e[i].value === n) {
					e[i].selected = !0, r && (e[i].defaultSelected = !0);
					return;
				}
				t !== null || e[i].disabled || (t = e[i]);
			}
			t !== null && (t.selected = !0);
		}
	}
	function Kt(e, t, n) {
		if (t != null && (t = "" + Ft(t), t !== e.value && (e.value = t), n == null)) {
			e.defaultValue !== t && (e.defaultValue = t);
			return;
		}
		e.defaultValue = n == null ? "" : "" + Ft(n);
	}
	function qt(e, t, n, r) {
		if (t == null) {
			if (r != null) {
				if (n != null) throw Error(a(92));
				if (ie(r)) {
					if (1 < r.length) throw Error(a(93));
					r = r[0];
				}
				n = r;
			}
			n ??= "", t = n;
		}
		n = Ft(t), e.defaultValue = n, r = e.textContent, r === n && r !== "" && r !== null && (e.value = r), Rt(e);
	}
	function Jt(e, t) {
		if (t) {
			var n = e.firstChild;
			if (n && n === e.lastChild && n.nodeType === 3) {
				n.nodeValue = t;
				return;
			}
		}
		e.textContent = t;
	}
	var Yt = new Set("animationIterationCount aspectRatio borderImageOutset borderImageSlice borderImageWidth boxFlex boxFlexGroup boxOrdinalGroup columnCount columns flex flexGrow flexPositive flexShrink flexNegative flexOrder gridArea gridRow gridRowEnd gridRowSpan gridRowStart gridColumn gridColumnEnd gridColumnSpan gridColumnStart fontWeight lineClamp lineHeight opacity order orphans scale tabSize widows zIndex zoom fillOpacity floodOpacity stopOpacity strokeDasharray strokeDashoffset strokeMiterlimit strokeOpacity strokeWidth MozAnimationIterationCount MozBoxFlex MozBoxFlexGroup MozLineClamp msAnimationIterationCount msFlex msZoom msFlexGrow msFlexNegative msFlexOrder msFlexPositive msFlexShrink msGridColumn msGridColumnSpan msGridRow msGridRowSpan WebkitAnimationIterationCount WebkitBoxFlex WebKitBoxFlexGroup WebkitBoxOrdinalGroup WebkitColumnCount WebkitColumns WebkitFlex WebkitFlexGrow WebkitFlexPositive WebkitFlexShrink WebkitLineClamp".split(" "));
	function Xt(e, t, n) {
		var r = t.indexOf("--") === 0;
		n == null || typeof n == "boolean" || n === "" ? r ? e.setProperty(t, "") : t === "float" ? e.cssFloat = "" : e[t] = "" : r ? e.setProperty(t, n) : typeof n != "number" || n === 0 || Yt.has(t) ? t === "float" ? e.cssFloat = n : e[t] = ("" + n).trim() : e[t] = n + "px";
	}
	function Zt(e, t, n) {
		if (t != null && typeof t != "object") throw Error(a(62));
		if (e = e.style, n != null) {
			for (var r in n) !n.hasOwnProperty(r) || t != null && t.hasOwnProperty(r) || (r.indexOf("--") === 0 ? e.setProperty(r, "") : r === "float" ? e.cssFloat = "" : e[r] = "");
			for (var i in t) r = t[i], t.hasOwnProperty(i) && n[i] !== r && Xt(e, i, r);
		} else for (var o in t) t.hasOwnProperty(o) && Xt(e, o, t[o]);
	}
	function Qt(e) {
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
	var $t = /* @__PURE__ */ new Map([
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
	]), en = /^[\u0000-\u001F ]*j[\r\n\t]*a[\r\n\t]*v[\r\n\t]*a[\r\n\t]*s[\r\n\t]*c[\r\n\t]*r[\r\n\t]*i[\r\n\t]*p[\r\n\t]*t[\r\n\t]*:/i;
	function tn(e) {
		return en.test("" + e) ? "javascript:throw new Error('React has blocked a javascript: URL as a security precaution.')" : e;
	}
	function nn() {}
	var rn = null;
	function an(e) {
		return e = e.target || e.srcElement || window, e.correspondingUseElement && (e = e.correspondingUseElement), e.nodeType === 3 ? e.parentNode : e;
	}
	var on = null, I = null;
	function sn(e) {
		var t = xt(e);
		if (t && (e = t.stateNode)) {
			var n = e[ft] || null;
			a: switch (e = t.stateNode, t.type) {
				case "input":
					if (Ht(e, n.value, n.defaultValue, n.defaultValue, n.checked, n.defaultChecked, n.type, n.name), t = n.name, n.type === "radio" && t != null) {
						for (n = e; n.parentNode;) n = n.parentNode;
						for (n = n.querySelectorAll("input[name=\"" + F("" + t) + "\"][type=\"radio\"]"), t = 0; t < n.length; t++) {
							var r = n[t];
							if (r !== e && r.form === e.form) {
								var i = r[ft] || null;
								if (!i) throw Error(a(90));
								Ht(r, i.value, i.defaultValue, i.defaultValue, i.checked, i.defaultChecked, i.type, i.name);
							}
						}
						for (t = 0; t < n.length; t++) r = n[t], r.form === e.form && zt(r);
					}
					break a;
				case "textarea":
					Kt(e, n.value, n.defaultValue);
					break a;
				case "select": t = n.value, t != null && Gt(e, !!n.multiple, t, !1);
			}
		}
	}
	var cn = !1;
	function ln(e, t, n) {
		if (cn) return e(t, n);
		cn = !0;
		try {
			return e(t);
		} finally {
			if (cn = !1, (on !== null || I !== null) && (bu(), on && (t = on, e = I, I = on = null, sn(t), e))) for (t = 0; t < e.length; t++) sn(e[t]);
		}
	}
	function un(e, t) {
		var n = e.stateNode;
		if (n === null) return null;
		var r = n[ft] || null;
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
	var dn = !(typeof window > "u" || window.document === void 0 || window.document.createElement === void 0), fn = !1;
	if (dn) try {
		var pn = {};
		Object.defineProperty(pn, "passive", { get: function() {
			fn = !0;
		} }), window.addEventListener("test", pn, pn), window.removeEventListener("test", pn, pn);
	} catch {
		fn = !1;
	}
	var mn = null, hn = null, gn = null;
	function _n() {
		if (gn) return gn;
		var e, t = hn, n = t.length, r, i = "value" in mn ? mn.value : mn.textContent, a = i.length;
		for (e = 0; e < n && t[e] === i[e]; e++);
		var o = n - e;
		for (r = 1; r <= o && t[n - r] === i[a - r]; r++);
		return gn = i.slice(e, 1 < r ? 1 - r : void 0);
	}
	function vn(e) {
		var t = e.keyCode;
		return "charCode" in e ? (e = e.charCode, e === 0 && t === 13 && (e = 13)) : e = t, e === 10 && (e = 13), 32 <= e || e === 13 ? e : 0;
	}
	function yn() {
		return !0;
	}
	function bn() {
		return !1;
	}
	function xn(e) {
		function t(t, n, r, i, a) {
			for (var o in this._reactName = t, this._targetInst = r, this.type = n, this.nativeEvent = i, this.target = a, this.currentTarget = null, e) e.hasOwnProperty(o) && (t = e[o], this[o] = t ? t(i) : i[o]);
			return this.isDefaultPrevented = (i.defaultPrevented == null ? !1 === i.returnValue : i.defaultPrevented) ? yn : bn, this.isPropagationStopped = bn, this;
		}
		return h(t.prototype, {
			preventDefault: function() {
				this.defaultPrevented = !0;
				var e = this.nativeEvent;
				e && (e.preventDefault ? e.preventDefault() : typeof e.returnValue != "unknown" && (e.returnValue = !1), this.isDefaultPrevented = yn);
			},
			stopPropagation: function() {
				var e = this.nativeEvent;
				e && (e.stopPropagation ? e.stopPropagation() : typeof e.cancelBubble != "unknown" && (e.cancelBubble = !0), this.isPropagationStopped = yn);
			},
			persist: function() {},
			isPersistent: yn
		}), t;
	}
	var Sn = {
		eventPhase: 0,
		bubbles: 0,
		cancelable: 0,
		timeStamp: function(e) {
			return e.timeStamp || Date.now();
		},
		defaultPrevented: 0,
		isTrusted: 0
	}, Cn = xn(Sn), wn = h({}, Sn, {
		view: 0,
		detail: 0
	}), Tn = xn(wn), En, Dn, On, kn = h({}, wn, {
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
		getModifierState: Bn,
		button: 0,
		buttons: 0,
		relatedTarget: function(e) {
			return e.relatedTarget === void 0 ? e.fromElement === e.srcElement ? e.toElement : e.fromElement : e.relatedTarget;
		},
		movementX: function(e) {
			return "movementX" in e ? e.movementX : (e !== On && (On && e.type === "mousemove" ? (En = e.screenX - On.screenX, Dn = e.screenY - On.screenY) : Dn = En = 0, On = e), En);
		},
		movementY: function(e) {
			return "movementY" in e ? e.movementY : Dn;
		}
	}), An = xn(kn), jn = xn(h({}, kn, { dataTransfer: 0 })), Mn = xn(h({}, wn, { relatedTarget: 0 })), Nn = xn(h({}, Sn, {
		animationName: 0,
		elapsedTime: 0,
		pseudoElement: 0
	})), Pn = xn(h({}, Sn, { clipboardData: function(e) {
		return "clipboardData" in e ? e.clipboardData : window.clipboardData;
	} })), Fn = xn(h({}, Sn, { data: 0 })), In = {
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
	}, Ln = {
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
	}, Rn = {
		Alt: "altKey",
		Control: "ctrlKey",
		Meta: "metaKey",
		Shift: "shiftKey"
	};
	function zn(e) {
		var t = this.nativeEvent;
		return t.getModifierState ? t.getModifierState(e) : (e = Rn[e]) ? !!t[e] : !1;
	}
	function Bn() {
		return zn;
	}
	var Vn = xn(h({}, wn, {
		key: function(e) {
			if (e.key) {
				var t = In[e.key] || e.key;
				if (t !== "Unidentified") return t;
			}
			return e.type === "keypress" ? (e = vn(e), e === 13 ? "Enter" : String.fromCharCode(e)) : e.type === "keydown" || e.type === "keyup" ? Ln[e.keyCode] || "Unidentified" : "";
		},
		code: 0,
		location: 0,
		ctrlKey: 0,
		shiftKey: 0,
		altKey: 0,
		metaKey: 0,
		repeat: 0,
		locale: 0,
		getModifierState: Bn,
		charCode: function(e) {
			return e.type === "keypress" ? vn(e) : 0;
		},
		keyCode: function(e) {
			return e.type === "keydown" || e.type === "keyup" ? e.keyCode : 0;
		},
		which: function(e) {
			return e.type === "keypress" ? vn(e) : e.type === "keydown" || e.type === "keyup" ? e.keyCode : 0;
		}
	})), Hn = xn(h({}, kn, {
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
	})), Un = xn(h({}, wn, {
		touches: 0,
		targetTouches: 0,
		changedTouches: 0,
		altKey: 0,
		metaKey: 0,
		ctrlKey: 0,
		shiftKey: 0,
		getModifierState: Bn
	})), Wn = xn(h({}, Sn, {
		propertyName: 0,
		elapsedTime: 0,
		pseudoElement: 0
	})), Gn = xn(h({}, kn, {
		deltaX: function(e) {
			return "deltaX" in e ? e.deltaX : "wheelDeltaX" in e ? -e.wheelDeltaX : 0;
		},
		deltaY: function(e) {
			return "deltaY" in e ? e.deltaY : "wheelDeltaY" in e ? -e.wheelDeltaY : "wheelDelta" in e ? -e.wheelDelta : 0;
		},
		deltaZ: 0,
		deltaMode: 0
	})), Kn = xn(h({}, Sn, {
		newState: 0,
		oldState: 0
	})), qn = [
		9,
		13,
		27,
		32
	], Jn = dn && "CompositionEvent" in window, Yn = null;
	dn && "documentMode" in document && (Yn = document.documentMode);
	var Xn = dn && "TextEvent" in window && !Yn, Zn = dn && (!Jn || Yn && 8 < Yn && 11 >= Yn), Qn = " ", $n = !1;
	function er(e, t) {
		switch (e) {
			case "keyup": return qn.indexOf(t.keyCode) !== -1;
			case "keydown": return t.keyCode !== 229;
			case "keypress":
			case "mousedown":
			case "focusout": return !0;
			default: return !1;
		}
	}
	function tr(e) {
		return e = e.detail, typeof e == "object" && "data" in e ? e.data : null;
	}
	var nr = !1;
	function rr(e, t) {
		switch (e) {
			case "compositionend": return tr(t);
			case "keypress": return t.which === 32 ? ($n = !0, Qn) : null;
			case "textInput": return e = t.data, e === Qn && $n ? null : e;
			default: return null;
		}
	}
	function ir(e, t) {
		if (nr) return e === "compositionend" || !Jn && er(e, t) ? (e = _n(), gn = hn = mn = null, nr = !1, e) : null;
		switch (e) {
			case "paste": return null;
			case "keypress":
				if (!(t.ctrlKey || t.altKey || t.metaKey) || t.ctrlKey && t.altKey) {
					if (t.char && 1 < t.char.length) return t.char;
					if (t.which) return String.fromCharCode(t.which);
				}
				return null;
			case "compositionend": return Zn && t.locale !== "ko" ? null : t.data;
			default: return null;
		}
	}
	var ar = {
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
	function or(e) {
		var t = e && e.nodeName && e.nodeName.toLowerCase();
		return t === "input" ? !!ar[e.type] : t === "textarea";
	}
	function sr(e, t, n, r) {
		on ? I ? I.push(r) : I = [r] : on = r, t = Ed(t, "onChange"), 0 < t.length && (n = new Cn("onChange", "change", null, n, r), e.push({
			event: n,
			listeners: t
		}));
	}
	var cr = null, lr = null;
	function ur(e) {
		yd(e, 0);
	}
	function dr(e) {
		if (zt(St(e))) return e;
	}
	function fr(e, t) {
		if (e === "change") return t;
	}
	var pr = !1;
	if (dn) {
		var mr;
		if (dn) {
			var hr = "oninput" in document;
			if (!hr) {
				var gr = document.createElement("div");
				gr.setAttribute("oninput", "return;"), hr = typeof gr.oninput == "function";
			}
			mr = hr;
		} else mr = !1;
		pr = mr && (!document.documentMode || 9 < document.documentMode);
	}
	function _r() {
		cr && (cr.detachEvent("onpropertychange", vr), lr = cr = null);
	}
	function vr(e) {
		if (e.propertyName === "value" && dr(lr)) {
			var t = [];
			sr(t, lr, e, an(e)), ln(ur, t);
		}
	}
	function yr(e, t, n) {
		e === "focusin" ? (_r(), cr = t, lr = n, cr.attachEvent("onpropertychange", vr)) : e === "focusout" && _r();
	}
	function br(e) {
		if (e === "selectionchange" || e === "keyup" || e === "keydown") return dr(lr);
	}
	function xr(e, t) {
		if (e === "click") return dr(t);
	}
	function Sr(e, t) {
		if (e === "input" || e === "change") return dr(t);
	}
	function Cr(e, t) {
		return e === t && (e !== 0 || 1 / e == 1 / t) || e !== e && t !== t;
	}
	var wr = typeof Object.is == "function" ? Object.is : Cr;
	function Tr(e, t) {
		if (wr(e, t)) return !0;
		if (typeof e != "object" || !e || typeof t != "object" || !t) return !1;
		var n = Object.keys(e), r = Object.keys(t);
		if (n.length !== r.length) return !1;
		for (r = 0; r < n.length; r++) {
			var i = n[r];
			if (!Te.call(t, i) || !wr(e[i], t[i])) return !1;
		}
		return !0;
	}
	function Er(e) {
		for (; e && e.firstChild;) e = e.firstChild;
		return e;
	}
	function Dr(e, t) {
		var n = Er(e);
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
			n = Er(n);
		}
	}
	function Or(e, t) {
		return e && t ? e === t ? !0 : e && e.nodeType === 3 ? !1 : t && t.nodeType === 3 ? Or(e, t.parentNode) : "contains" in e ? e.contains(t) : e.compareDocumentPosition ? !!(e.compareDocumentPosition(t) & 16) : !1 : !1;
	}
	function kr(e) {
		e = e != null && e.ownerDocument != null && e.ownerDocument.defaultView != null ? e.ownerDocument.defaultView : window;
		for (var t = Bt(e.document); t instanceof e.HTMLIFrameElement;) {
			try {
				var n = typeof t.contentWindow.location.href == "string";
			} catch {
				n = !1;
			}
			if (n) e = t.contentWindow;
			else break;
			t = Bt(e.document);
		}
		return t;
	}
	function Ar(e) {
		var t = e && e.nodeName && e.nodeName.toLowerCase();
		return t && (t === "input" && (e.type === "text" || e.type === "search" || e.type === "tel" || e.type === "url" || e.type === "password") || t === "textarea" || e.contentEditable === "true");
	}
	var jr = dn && "documentMode" in document && 11 >= document.documentMode, Mr = null, Nr = null, Pr = null, Fr = !1;
	function Ir(e, t, n) {
		var r = n.window === n ? n.document : n.nodeType === 9 ? n : n.ownerDocument;
		Fr || Mr == null || Mr !== Bt(r) || (r = Mr, "selectionStart" in r && Ar(r) ? r = {
			start: r.selectionStart,
			end: r.selectionEnd
		} : (r = (r.ownerDocument && r.ownerDocument.defaultView || window).getSelection(), r = {
			anchorNode: r.anchorNode,
			anchorOffset: r.anchorOffset,
			focusNode: r.focusNode,
			focusOffset: r.focusOffset
		}), Pr && Tr(Pr, r) || (Pr = r, r = Ed(Nr, "onSelect"), 0 < r.length && (t = new Cn("onSelect", "select", null, t, n), e.push({
			event: t,
			listeners: r
		}), t.target = Mr)));
	}
	function Lr(e, t) {
		var n = {};
		return n[e.toLowerCase()] = t.toLowerCase(), n["Webkit" + e] = "webkit" + t, n["Moz" + e] = "moz" + t, n;
	}
	var Rr = {
		animationend: Lr("Animation", "AnimationEnd"),
		animationiteration: Lr("Animation", "AnimationIteration"),
		animationstart: Lr("Animation", "AnimationStart"),
		transitionrun: Lr("Transition", "TransitionRun"),
		transitionstart: Lr("Transition", "TransitionStart"),
		transitioncancel: Lr("Transition", "TransitionCancel"),
		transitionend: Lr("Transition", "TransitionEnd")
	}, zr = {}, Br = {};
	dn && (Br = document.createElement("div").style, "AnimationEvent" in window || (delete Rr.animationend.animation, delete Rr.animationiteration.animation, delete Rr.animationstart.animation), "TransitionEvent" in window || delete Rr.transitionend.transition);
	function Vr(e) {
		if (zr[e]) return zr[e];
		if (!Rr[e]) return e;
		var t = Rr[e], n;
		for (n in t) if (t.hasOwnProperty(n) && n in Br) return zr[e] = t[n];
		return e;
	}
	var Hr = Vr("animationend"), Ur = Vr("animationiteration"), Wr = Vr("animationstart"), Gr = Vr("transitionrun"), Kr = Vr("transitionstart"), qr = Vr("transitioncancel"), Jr = Vr("transitionend"), Yr = /* @__PURE__ */ new Map(), Xr = "abort auxClick beforeToggle cancel canPlay canPlayThrough click close contextMenu copy cut drag dragEnd dragEnter dragExit dragLeave dragOver dragStart drop durationChange emptied encrypted ended error gotPointerCapture input invalid keyDown keyPress keyUp load loadedData loadedMetadata loadStart lostPointerCapture mouseDown mouseMove mouseOut mouseOver mouseUp paste pause play playing pointerCancel pointerDown pointerMove pointerOut pointerOver pointerUp progress rateChange reset resize seeked seeking stalled submit suspend timeUpdate touchCancel touchEnd touchStart volumeChange scroll toggle touchMove waiting wheel".split(" ");
	Xr.push("scrollEnd");
	function Zr(e, t) {
		Yr.set(e, t), Et(t, [e]);
	}
	var Qr = typeof reportError == "function" ? reportError : function(e) {
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
	}, $r = [], ei = 0, ti = 0;
	function ni() {
		for (var e = ei, t = ti = ei = 0; t < e;) {
			var n = $r[t];
			$r[t++] = null;
			var r = $r[t];
			$r[t++] = null;
			var i = $r[t];
			$r[t++] = null;
			var a = $r[t];
			if ($r[t++] = null, r !== null && i !== null) {
				var o = r.pending;
				o === null ? i.next = i : (i.next = o.next, o.next = i), r.pending = i;
			}
			a !== 0 && oi(n, i, a);
		}
	}
	function ri(e, t, n, r) {
		$r[ei++] = e, $r[ei++] = t, $r[ei++] = n, $r[ei++] = r, ti |= r, e.lanes |= r, e = e.alternate, e !== null && (e.lanes |= r);
	}
	function ii(e, t, n, r) {
		return ri(e, t, n, r), si(e);
	}
	function ai(e, t) {
		return ri(e, null, null, t), si(e);
	}
	function oi(e, t, n) {
		e.lanes |= n;
		var r = e.alternate;
		r !== null && (r.lanes |= n);
		for (var i = !1, a = e.return; a !== null;) a.childLanes |= n, r = a.alternate, r !== null && (r.childLanes |= n), a.tag === 22 && (e = a.stateNode, e === null || e._visibility & 1 || (i = !0)), e = a, a = a.return;
		return e.tag === 3 ? (a = e.stateNode, i && t !== null && (i = 31 - He(n), e = a.hiddenUpdates, r = e[i], r === null ? e[i] = [t] : r.push(t), t.lane = n | 536870912), a) : null;
	}
	function si(e) {
		if (50 < du) throw du = 0, fu = null, Error(a(185));
		for (var t = e.return; t !== null;) e = t, t = e.return;
		return e.tag === 3 ? e.stateNode : null;
	}
	var ci = {};
	function li(e, t, n, r) {
		this.tag = e, this.key = n, this.sibling = this.child = this.return = this.stateNode = this.type = this.elementType = null, this.index = 0, this.refCleanup = this.ref = null, this.pendingProps = t, this.dependencies = this.memoizedState = this.updateQueue = this.memoizedProps = null, this.mode = r, this.subtreeFlags = this.flags = 0, this.deletions = null, this.childLanes = this.lanes = 0, this.alternate = null;
	}
	function ui(e, t, n, r) {
		return new li(e, t, n, r);
	}
	function di(e) {
		return e = e.prototype, !(!e || !e.isReactComponent);
	}
	function fi(e, t) {
		var n = e.alternate;
		return n === null ? (n = ui(e.tag, t, e.key, e.mode), n.elementType = e.elementType, n.type = e.type, n.stateNode = e.stateNode, n.alternate = e, e.alternate = n) : (n.pendingProps = t, n.type = e.type, n.flags = 0, n.subtreeFlags = 0, n.deletions = null), n.flags = e.flags & 65011712, n.childLanes = e.childLanes, n.lanes = e.lanes, n.child = e.child, n.memoizedProps = e.memoizedProps, n.memoizedState = e.memoizedState, n.updateQueue = e.updateQueue, t = e.dependencies, n.dependencies = t === null ? null : {
			lanes: t.lanes,
			firstContext: t.firstContext
		}, n.sibling = e.sibling, n.index = e.index, n.ref = e.ref, n.refCleanup = e.refCleanup, n;
	}
	function pi(e, t) {
		e.flags &= 65011714;
		var n = e.alternate;
		return n === null ? (e.childLanes = 0, e.lanes = t, e.child = null, e.subtreeFlags = 0, e.memoizedProps = null, e.memoizedState = null, e.updateQueue = null, e.dependencies = null, e.stateNode = null) : (e.childLanes = n.childLanes, e.lanes = n.lanes, e.child = n.child, e.subtreeFlags = 0, e.deletions = null, e.memoizedProps = n.memoizedProps, e.memoizedState = n.memoizedState, e.updateQueue = n.updateQueue, e.type = n.type, t = n.dependencies, e.dependencies = t === null ? null : {
			lanes: t.lanes,
			firstContext: t.firstContext
		}), e;
	}
	function mi(e, t, n, r, i, o) {
		var s = 0;
		if (r = e, typeof e == "function") di(e) && (s = 1);
		else if (typeof e == "string") s = Uf(e, n, ue.current) ? 26 : e === "html" || e === "head" || e === "body" ? 27 : 5;
		else a: switch (e) {
			case O: return e = ui(31, n, t, i), e.elementType = O, e.lanes = o, e;
			case y: return hi(n.children, i, o, t);
			case b:
				s = 8, i |= 24;
				break;
			case x: return e = ui(12, n, t, i | 2), e.elementType = x, e.lanes = o, e;
			case T: return e = ui(13, n, t, i), e.elementType = T, e.lanes = o, e;
			case E: return e = ui(19, n, t, i), e.elementType = E, e.lanes = o, e;
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
					case ee:
						s = 14;
						break a;
					case D:
						s = 16, r = null;
						break a;
				}
				s = 29, n = Error(a(130, e === null ? "null" : typeof e, "")), r = null;
		}
		return t = ui(s, n, t, i), t.elementType = e, t.type = r, t.lanes = o, t;
	}
	function hi(e, t, n, r) {
		return e = ui(7, e, r, t), e.lanes = n, e;
	}
	function gi(e, t, n) {
		return e = ui(6, e, null, t), e.lanes = n, e;
	}
	function _i(e) {
		var t = ui(18, null, null, 0);
		return t.stateNode = e, t;
	}
	function vi(e, t, n) {
		return t = ui(4, e.children === null ? [] : e.children, e.key, t), t.lanes = n, t.stateNode = {
			containerInfo: e.containerInfo,
			pendingChildren: null,
			implementation: e.implementation
		}, t;
	}
	var yi = /* @__PURE__ */ new WeakMap();
	function bi(e, t) {
		if (typeof e == "object" && e) {
			var n = yi.get(e);
			return n === void 0 ? (t = {
				value: e,
				source: t,
				stack: we(t)
			}, yi.set(e, t), t) : n;
		}
		return {
			value: e,
			source: t,
			stack: we(t)
		};
	}
	var xi = [], Si = 0, Ci = null, wi = 0, Ti = [], Ei = 0, Di = null, Oi = 1, ki = "";
	function Ai(e, t) {
		xi[Si++] = wi, xi[Si++] = Ci, Ci = e, wi = t;
	}
	function ji(e, t, n) {
		Ti[Ei++] = Oi, Ti[Ei++] = ki, Ti[Ei++] = Di, Di = e;
		var r = Oi;
		e = ki;
		var i = 32 - He(r) - 1;
		r &= ~(1 << i), n += 1;
		var a = 32 - He(t) + i;
		if (30 < a) {
			var o = i - i % 5;
			a = (r & (1 << o) - 1).toString(32), r >>= o, i -= o, Oi = 1 << 32 - He(t) + i | n << i | r, ki = a + e;
		} else Oi = 1 << a | n << i | r, ki = e;
	}
	function Mi(e) {
		e.return !== null && (Ai(e, 1), ji(e, 1, 0));
	}
	function Ni(e) {
		for (; e === Ci;) Ci = xi[--Si], xi[Si] = null, wi = xi[--Si], xi[Si] = null;
		for (; e === Di;) Di = Ti[--Ei], Ti[Ei] = null, ki = Ti[--Ei], Ti[Ei] = null, Oi = Ti[--Ei], Ti[Ei] = null;
	}
	function Pi(e, t) {
		Ti[Ei++] = Oi, Ti[Ei++] = ki, Ti[Ei++] = Di, Oi = t.id, ki = t.overflow, Di = e;
	}
	var Fi = null, L = null, R = !1, Ii = null, Li = !1, Ri = Error(a(519));
	function zi(e) {
		throw Gi(bi(Error(a(418, 1 < arguments.length && arguments[1] !== void 0 && arguments[1] ? "text" : "HTML", "")), e)), Ri;
	}
	function Bi(e) {
		var t = e.stateNode, n = e.type, r = e.memoizedProps;
		switch (t[dt] = e, t[ft] = r, n) {
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
				Q("invalid", t), Ut(t, r.value, r.defaultValue, r.checked, r.defaultChecked, r.type, r.name, !0);
				break;
			case "select":
				Q("invalid", t);
				break;
			case "textarea": Q("invalid", t), qt(t, r.value, r.defaultValue, r.children);
		}
		n = r.children, typeof n != "string" && typeof n != "number" && typeof n != "bigint" || t.textContent === "" + n || !0 === r.suppressHydrationWarning || Md(t.textContent, n) ? (r.popover != null && (Q("beforetoggle", t), Q("toggle", t)), r.onScroll != null && Q("scroll", t), r.onScrollEnd != null && Q("scrollend", t), r.onClick != null && (t.onclick = nn), t = !0) : t = !1, t || zi(e, !0);
	}
	function Vi(e) {
		for (Fi = e.return; Fi;) switch (Fi.tag) {
			case 5:
			case 31:
			case 13:
				Li = !1;
				return;
			case 27:
			case 3:
				Li = !0;
				return;
			default: Fi = Fi.return;
		}
	}
	function Hi(e) {
		if (e !== Fi) return !1;
		if (!R) return Vi(e), R = !0, !1;
		var t = e.tag, n;
		if ((n = t !== 3 && t !== 27) && ((n = t === 5) && (n = e.type, n = n === "form" || n === "button" || Ud(e.type, e.memoizedProps)), n = !n), n && L && zi(e), Vi(e), t === 13) {
			if (e = e.memoizedState, e = e === null ? null : e.dehydrated, !e) throw Error(a(317));
			L = uf(e);
		} else if (t === 31) {
			if (e = e.memoizedState, e = e === null ? null : e.dehydrated, !e) throw Error(a(317));
			L = uf(e);
		} else t === 27 ? (t = L, Zd(e.type) ? (e = lf, lf = null, L = e) : L = t) : L = Fi ? cf(e.stateNode.nextSibling) : null;
		return !0;
	}
	function Ui() {
		L = Fi = null, R = !1;
	}
	function Wi() {
		var e = Ii;
		return e !== null && (Zl === null ? Zl = e : Zl.push.apply(Zl, e), Ii = null), e;
	}
	function Gi(e) {
		Ii === null ? Ii = [e] : Ii.push(e);
	}
	var Ki = ce(null), qi = null, Ji = null;
	function Yi(e, t, n) {
		N(Ki, t._currentValue), t._currentValue = n;
	}
	function Xi(e) {
		e._currentValue = Ki.current, le(Ki);
	}
	function Zi(e, t, n) {
		for (; e !== null;) {
			var r = e.alternate;
			if ((e.childLanes & t) === t ? r !== null && (r.childLanes & t) !== t && (r.childLanes |= t) : (e.childLanes |= t, r !== null && (r.childLanes |= t)), e === n) break;
			e = e.return;
		}
	}
	function Qi(e, t, n, r) {
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
						o.lanes |= n, c = o.alternate, c !== null && (c.lanes |= n), Zi(o.return, n, e), r || (s = null);
						break a;
					}
					o = c.next;
				}
			} else if (i.tag === 18) {
				if (s = i.return, s === null) throw Error(a(341));
				s.lanes |= n, o = s.alternate, o !== null && (o.lanes |= n), Zi(s, n, e), s = null;
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
	function $i(e, t, n, r) {
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
					wr(i.pendingProps.value, s.value) || (e === null ? e = [c] : e.push(c));
				}
			} else if (i === pe.current) {
				if (s = i.alternate, s === null) throw Error(a(387));
				s.memoizedState.memoizedState !== i.memoizedState.memoizedState && (e === null ? e = [Qf] : e.push(Qf));
			}
			i = i.return;
		}
		e !== null && Qi(t, e, n, r), t.flags |= 262144;
	}
	function ea(e) {
		for (e = e.firstContext; e !== null;) {
			if (!wr(e.context._currentValue, e.memoizedValue)) return !0;
			e = e.next;
		}
		return !1;
	}
	function ta(e) {
		qi = e, Ji = null, e = e.dependencies, e !== null && (e.firstContext = null);
	}
	function na(e) {
		return ia(qi, e);
	}
	function ra(e, t) {
		return qi === null && ta(e), ia(e, t);
	}
	function ia(e, t) {
		var n = t._currentValue;
		if (t = {
			context: t,
			memoizedValue: n,
			next: null
		}, Ji === null) {
			if (e === null) throw Error(a(308));
			Ji = t, e.dependencies = {
				lanes: 0,
				firstContext: t
			}, e.flags |= 524288;
		} else Ji = Ji.next = t;
		return n;
	}
	var aa = typeof AbortController < "u" ? AbortController : function() {
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
	}, oa = t.unstable_scheduleCallback, sa = t.unstable_NormalPriority, ca = {
		$$typeof: C,
		Consumer: null,
		Provider: null,
		_currentValue: null,
		_currentValue2: null,
		_threadCount: 0
	};
	function la() {
		return {
			controller: new aa(),
			data: /* @__PURE__ */ new Map(),
			refCount: 0
		};
	}
	function ua(e) {
		e.refCount--, e.refCount === 0 && oa(sa, function() {
			e.controller.abort();
		});
	}
	var da = null, fa = 0, pa = 0, ma = null;
	function ha(e, t) {
		if (da === null) {
			var n = da = [];
			fa = 0, pa = dd(), ma = {
				status: "pending",
				value: void 0,
				then: function(e) {
					n.push(e);
				}
			};
		}
		return fa++, t.then(ga, ga), t;
	}
	function ga() {
		if (--fa === 0 && da !== null) {
			ma !== null && (ma.status = "fulfilled");
			var e = da;
			da = null, pa = 0, ma = null;
			for (var t = 0; t < e.length; t++) (0, e[t])();
		}
	}
	function _a(e, t) {
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
	var va = j.S;
	j.S = function(e, t) {
		eu = Ae(), typeof t == "object" && t && typeof t.then == "function" && ha(e, t), va !== null && va(e, t);
	};
	var z = ce(null);
	function ya() {
		var e = z.current;
		return e === null ? q.pooledCache : e;
	}
	function ba(e, t) {
		t === null ? N(z, z.current) : N(z, t.pool);
	}
	function B() {
		var e = ya();
		return e === null ? null : {
			parent: ca._currentValue,
			pool: e
		};
	}
	var V = Error(a(460)), xa = Error(a(474)), Sa = Error(a(542)), Ca = { then: function() {} };
	function wa(e) {
		return e = e.status, e === "fulfilled" || e === "rejected";
	}
	function Ta(e, t, n) {
		switch (n = e[n], n === void 0 ? e.push(t) : n !== t && (t.then(nn, nn), t = n), t.status) {
			case "fulfilled": return t.value;
			case "rejected": throw e = t.reason, ka(e), e;
			default:
				if (typeof t.status == "string") t.then(nn, nn);
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
					case "rejected": throw e = t.reason, ka(e), e;
				}
				throw Da = t, V;
		}
	}
	function Ea(e) {
		try {
			var t = e._init;
			return t(e._payload);
		} catch (e) {
			throw typeof e == "object" && e && typeof e.then == "function" ? (Da = e, V) : e;
		}
	}
	var Da = null;
	function Oa() {
		if (Da === null) throw Error(a(459));
		var e = Da;
		return Da = null, e;
	}
	function ka(e) {
		if (e === V || e === Sa) throw Error(a(483));
	}
	var Aa = null, ja = 0;
	function Ma(e) {
		var t = ja;
		return ja += 1, Aa === null && (Aa = []), Ta(Aa, e, t);
	}
	function Na(e, t) {
		t = t.props.ref, e.ref = t === void 0 ? null : t;
	}
	function Pa(e, t) {
		throw t.$$typeof === g ? Error(a(525)) : (e = Object.prototype.toString.call(t), Error(a(31, e === "[object Object]" ? "object with keys {" + Object.keys(t).join(", ") + "}" : e)));
	}
	function Fa(e) {
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
			return e = fi(e, t), e.index = 0, e.sibling = null, e;
		}
		function o(t, n, r) {
			return t.index = r, e ? (r = t.alternate, r === null ? (t.flags |= 67108866, n) : (r = r.index, r < n ? (t.flags |= 67108866, n) : r)) : (t.flags |= 1048576, n);
		}
		function s(t) {
			return e && t.alternate === null && (t.flags |= 67108866), t;
		}
		function c(e, t, n, r) {
			return t === null || t.tag !== 6 ? (t = gi(n, e.mode, r), t.return = e, t) : (t = i(t, n), t.return = e, t);
		}
		function l(e, t, n, r) {
			var a = n.type;
			return a === y ? d(e, t, n.props.children, r, n.key) : t !== null && (t.elementType === a || typeof a == "object" && a && a.$$typeof === D && Ea(a) === t.type) ? (t = i(t, n.props), Na(t, n), t.return = e, t) : (t = mi(n.type, n.key, n.props, null, e.mode, r), Na(t, n), t.return = e, t);
		}
		function u(e, t, n, r) {
			return t === null || t.tag !== 4 || t.stateNode.containerInfo !== n.containerInfo || t.stateNode.implementation !== n.implementation ? (t = vi(n, e.mode, r), t.return = e, t) : (t = i(t, n.children || []), t.return = e, t);
		}
		function d(e, t, n, r, a) {
			return t === null || t.tag !== 7 ? (t = hi(n, e.mode, r, a), t.return = e, t) : (t = i(t, n), t.return = e, t);
		}
		function f(e, t, n) {
			if (typeof t == "string" && t !== "" || typeof t == "number" || typeof t == "bigint") return t = gi("" + t, e.mode, n), t.return = e, t;
			if (typeof t == "object" && t) {
				switch (t.$$typeof) {
					case _: return n = mi(t.type, t.key, t.props, null, e.mode, n), Na(n, t), n.return = e, n;
					case v: return t = vi(t, e.mode, n), t.return = e, t;
					case D: return t = Ea(t), f(e, t, n);
				}
				if (ie(t) || A(t)) return t = hi(t, e.mode, n, null), t.return = e, t;
				if (typeof t.then == "function") return f(e, Ma(t), n);
				if (t.$$typeof === C) return f(e, ra(e, t), n);
				Pa(e, t);
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
					case D: return n = Ea(n), p(e, t, n, r);
				}
				if (ie(n) || A(n)) return i === null ? d(e, t, n, r, null) : null;
				if (typeof n.then == "function") return p(e, t, Ma(n), r);
				if (n.$$typeof === C) return p(e, t, ra(e, n), r);
				Pa(e, n);
			}
			return null;
		}
		function m(e, t, n, r, i) {
			if (typeof r == "string" && r !== "" || typeof r == "number" || typeof r == "bigint") return e = e.get(n) || null, c(t, e, "" + r, i);
			if (typeof r == "object" && r) {
				switch (r.$$typeof) {
					case _: return e = e.get(r.key === null ? n : r.key) || null, l(t, e, r, i);
					case v: return e = e.get(r.key === null ? n : r.key) || null, u(t, e, r, i);
					case D: return r = Ea(r), m(e, t, n, r, i);
				}
				if (ie(r) || A(r)) return e = e.get(n) || null, d(t, e, r, i, null);
				if (typeof r.then == "function") return m(e, t, n, Ma(r), i);
				if (r.$$typeof === C) return m(e, t, n, ra(t, r), i);
				Pa(t, r);
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
			if (h === s.length) return n(i, d), R && Ai(i, h), l;
			if (d === null) {
				for (; h < s.length; h++) d = f(i, s[h], c), d !== null && (a = o(d, a, h), u === null ? l = d : u.sibling = d, u = d);
				return R && Ai(i, h), l;
			}
			for (d = r(d); h < s.length; h++) g = m(d, i, h, s[h], c), g !== null && (e && g.alternate !== null && d.delete(g.key === null ? h : g.key), a = o(g, a, h), u === null ? l = g : u.sibling = g, u = g);
			return e && d.forEach(function(e) {
				return t(i, e);
			}), R && Ai(i, h), l;
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
			if (v.done) return n(i, h), R && Ai(i, g), u;
			if (h === null) {
				for (; !v.done; g++, v = c.next()) v = f(i, v.value, l), v !== null && (s = o(v, s, g), d === null ? u = v : d.sibling = v, d = v);
				return R && Ai(i, g), u;
			}
			for (h = r(h); !v.done; g++, v = c.next()) v = m(h, i, g, v.value, l), v !== null && (e && v.alternate !== null && h.delete(v.key === null ? g : v.key), s = o(v, s, g), d === null ? u = v : d.sibling = v, d = v);
			return e && h.forEach(function(e) {
				return t(i, e);
			}), R && Ai(i, g), u;
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
									} else if (r.elementType === l || typeof l == "object" && l && l.$$typeof === D && Ea(l) === r.type) {
										n(e, r.sibling), c = i(r, o.props), Na(c, o), c.return = e, e = c;
										break a;
									}
									n(e, r);
									break;
								}
								t(e, r), r = r.sibling;
							}
							o.type === y ? (c = hi(o.props.children, e.mode, c, o.key), c.return = e, e = c) : (c = mi(o.type, o.key, o.props, null, e.mode, c), Na(c, o), c.return = e, e = c);
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
							c = vi(o, e.mode, c), c.return = e, e = c;
						}
						return s(e);
					case D: return o = Ea(o), b(e, r, o, c);
				}
				if (ie(o)) return h(e, r, o, c);
				if (A(o)) {
					if (l = A(o), typeof l != "function") throw Error(a(150));
					return o = l.call(o), g(e, r, o, c);
				}
				if (typeof o.then == "function") return b(e, r, Ma(o), c);
				if (o.$$typeof === C) return b(e, r, ra(e, o), c);
				Pa(e, o);
			}
			return typeof o == "string" && o !== "" || typeof o == "number" || typeof o == "bigint" ? (o = "" + o, r !== null && r.tag === 6 ? (n(e, r.sibling), c = i(r, o), c.return = e, e = c) : (n(e, r), c = gi(o, e.mode, c), c.return = e, e = c), s(e)) : n(e, r);
		}
		return function(e, t, n, r) {
			try {
				ja = 0;
				var i = b(e, t, n, r);
				return Aa = null, i;
			} catch (t) {
				if (t === V || t === Sa) throw t;
				var a = ui(29, t, null, e.mode);
				return a.lanes = r, a.return = e, a;
			}
		};
	}
	var Ia = Fa(!0), La = Fa(!1), Ra = !1;
	function za(e) {
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
	function Ba(e, t) {
		e = e.updateQueue, t.updateQueue === e && (t.updateQueue = {
			baseState: e.baseState,
			firstBaseUpdate: e.firstBaseUpdate,
			lastBaseUpdate: e.lastBaseUpdate,
			shared: e.shared,
			callbacks: null
		});
	}
	function Va(e) {
		return {
			lane: e,
			tag: 0,
			payload: null,
			callback: null,
			next: null
		};
	}
	function Ha(e, t, n) {
		var r = e.updateQueue;
		if (r === null) return null;
		if (r = r.shared, K & 2) {
			var i = r.pending;
			return i === null ? t.next = t : (t.next = i.next, i.next = t), r.pending = t, t = si(e), oi(e, null, n), t;
		}
		return ri(e, r, t, n), si(e);
	}
	function Ua(e, t, n) {
		if (t = t.updateQueue, t !== null && (t = t.shared, n & 4194048)) {
			var r = t.lanes;
			r &= e.pendingLanes, n |= r, t.lanes = n, it(e, n);
		}
	}
	function Wa(e, t) {
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
	var Ga = !1;
	function Ka() {
		if (Ga) {
			var e = ma;
			if (e !== null) throw e;
		}
	}
	function qa(e, t, n, r) {
		Ga = !1;
		var i = e.updateQueue;
		Ra = !1;
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
					f !== 0 && f === pa && (Ga = !0), u !== null && (u = u.next = {
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
							case 2: Ra = !0;
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
	function Ja(e, t) {
		if (typeof e != "function") throw Error(a(191, e));
		e.call(t);
	}
	function Ya(e, t) {
		var n = e.callbacks;
		if (n !== null) for (e.callbacks = null, e = 0; e < n.length; e++) Ja(n[e], t);
	}
	var Xa = ce(null), Za = ce(0);
	function Qa(e, t) {
		e = Ul, N(Za, e), N(Xa, t), Ul = e | t.baseLanes;
	}
	function $a() {
		N(Za, Ul), N(Xa, Xa.current);
	}
	function eo() {
		Ul = Za.current, le(Xa), le(Za);
	}
	var to = ce(null), no = null;
	function ro(e) {
		var t = e.alternate;
		N(co, co.current & 1), N(to, e), no === null && (t === null || Xa.current !== null || t.memoizedState !== null) && (no = e);
	}
	function io(e) {
		N(co, co.current), N(to, e), no === null && (no = e);
	}
	function ao(e) {
		e.tag === 22 ? (N(co, co.current), N(to, e), no === null && (no = e)) : oo(e);
	}
	function oo() {
		N(co, co.current), N(to, to.current);
	}
	function so(e) {
		le(to), no === e && (no = null), le(co);
	}
	var co = ce(0);
	function lo(e) {
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
	var uo = 0, H = null, U = null, fo = null, po = !1, mo = !1, ho = !1, go = 0, _o = 0, vo = null, yo = 0;
	function bo() {
		throw Error(a(321));
	}
	function xo(e, t) {
		if (t === null) return !1;
		for (var n = 0; n < t.length && n < e.length; n++) if (!wr(e[n], t[n])) return !1;
		return !0;
	}
	function So(e, t, n, r, i, a) {
		return uo = a, H = t, t.memoizedState = null, t.updateQueue = null, t.lanes = 0, j.H = e === null || e.memoizedState === null ? zs : Bs, ho = !1, a = n(r, i), ho = !1, mo && (a = wo(t, n, r, i)), Co(e), a;
	}
	function Co(e) {
		j.H = Rs;
		var t = U !== null && U.next !== null;
		if (uo = 0, fo = U = H = null, po = !1, _o = 0, vo = null, t) throw Error(a(300));
		e === null || rc || (e = e.dependencies, e !== null && ea(e) && (rc = !0));
	}
	function wo(e, t, n, r) {
		H = e;
		var i = 0;
		do {
			if (mo && (vo = null), _o = 0, mo = !1, 25 <= i) throw Error(a(301));
			if (i += 1, fo = U = null, e.updateQueue != null) {
				var o = e.updateQueue;
				o.lastEffect = null, o.events = null, o.stores = null, o.memoCache != null && (o.memoCache.index = 0);
			}
			j.H = Vs, o = t(n, r);
		} while (mo);
		return o;
	}
	function To() {
		var e = j.H, t = e.useState()[0];
		return t = typeof t.then == "function" ? Mo(t) : t, e = e.useState()[0], (U === null ? null : U.memoizedState) !== e && (H.flags |= 1024), t;
	}
	function Eo() {
		var e = go !== 0;
		return go = 0, e;
	}
	function Do(e, t, n) {
		t.updateQueue = e.updateQueue, t.flags &= -2053, e.lanes &= ~n;
	}
	function Oo(e) {
		if (po) {
			for (e = e.memoizedState; e !== null;) {
				var t = e.queue;
				t !== null && (t.pending = null), e = e.next;
			}
			po = !1;
		}
		uo = 0, fo = U = H = null, mo = !1, _o = go = 0, vo = null;
	}
	function ko() {
		var e = {
			memoizedState: null,
			baseState: null,
			baseQueue: null,
			queue: null,
			next: null
		};
		return fo === null ? H.memoizedState = fo = e : fo = fo.next = e, fo;
	}
	function Ao() {
		if (U === null) {
			var e = H.alternate;
			e = e === null ? null : e.memoizedState;
		} else e = U.next;
		var t = fo === null ? H.memoizedState : fo.next;
		if (t !== null) fo = t, U = e;
		else {
			if (e === null) throw H.alternate === null ? Error(a(467)) : Error(a(310));
			U = e, e = {
				memoizedState: U.memoizedState,
				baseState: U.baseState,
				baseQueue: U.baseQueue,
				queue: U.queue,
				next: null
			}, fo === null ? H.memoizedState = fo = e : fo = fo.next = e;
		}
		return fo;
	}
	function jo() {
		return {
			lastEffect: null,
			events: null,
			stores: null,
			memoCache: null
		};
	}
	function Mo(e) {
		var t = _o;
		return _o += 1, vo === null && (vo = []), e = Ta(vo, e, t), t = H, (fo === null ? t.memoizedState : fo.next) === null && (t = t.alternate, j.H = t === null || t.memoizedState === null ? zs : Bs), e;
	}
	function No(e) {
		if (typeof e == "object" && e) {
			if (typeof e.then == "function") return Mo(e);
			if (e.$$typeof === C) return na(e);
		}
		throw Error(a(438, String(e)));
	}
	function Po(e) {
		var t = null, n = H.updateQueue;
		if (n !== null && (t = n.memoCache), t == null) {
			var r = H.alternate;
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
		}, n === null && (n = jo(), H.updateQueue = n), n.memoCache = t, n = t.data[t.index], n === void 0) for (n = t.data[t.index] = Array(e), r = 0; r < e; r++) n[r] = te;
		return t.index++, n;
	}
	function Fo(e, t) {
		return typeof t == "function" ? t(e) : t;
	}
	function Io(e) {
		return Lo(Ao(), U, e);
	}
	function Lo(e, t, n) {
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
				if (f === u.lane ? (uo & f) === f : (Y & f) === f) {
					var p = u.revertLane;
					if (p === 0) l !== null && (l = l.next = {
						lane: 0,
						revertLane: 0,
						gesture: null,
						action: u.action,
						hasEagerState: u.hasEagerState,
						eagerState: u.eagerState,
						next: null
					}), f === pa && (d = !0);
					else if ((uo & p) === p) {
						u = u.next, p === pa && (d = !0);
						continue;
					} else f = {
						lane: 0,
						revertLane: u.revertLane,
						gesture: null,
						action: u.action,
						hasEagerState: u.hasEagerState,
						eagerState: u.eagerState,
						next: null
					}, l === null ? (c = l = f, s = o) : l = l.next = f, H.lanes |= p, Gl |= p;
					f = u.action, ho && n(o, f), o = u.hasEagerState ? u.eagerState : n(o, f);
				} else p = {
					lane: f,
					revertLane: u.revertLane,
					gesture: u.gesture,
					action: u.action,
					hasEagerState: u.hasEagerState,
					eagerState: u.eagerState,
					next: null
				}, l === null ? (c = l = p, s = o) : l = l.next = p, H.lanes |= f, Gl |= f;
				u = u.next;
			} while (u !== null && u !== t);
			if (l === null ? s = o : l.next = c, !wr(o, e.memoizedState) && (rc = !0, d && (n = ma, n !== null))) throw n;
			e.memoizedState = o, e.baseState = s, e.baseQueue = l, r.lastRenderedState = o;
		}
		return i === null && (r.lanes = 0), [e.memoizedState, r.dispatch];
	}
	function Ro(e) {
		var t = Ao(), n = t.queue;
		if (n === null) throw Error(a(311));
		n.lastRenderedReducer = e;
		var r = n.dispatch, i = n.pending, o = t.memoizedState;
		if (i !== null) {
			n.pending = null;
			var s = i = i.next;
			do
				o = e(o, s.action), s = s.next;
			while (s !== i);
			wr(o, t.memoizedState) || (rc = !0), t.memoizedState = o, t.baseQueue === null && (t.baseState = o), n.lastRenderedState = o;
		}
		return [o, r];
	}
	function zo(e, t, n) {
		var r = H, i = Ao(), o = R;
		if (o) {
			if (n === void 0) throw Error(a(407));
			n = n();
		} else n = t();
		var s = !wr((U || i).memoizedState, n);
		if (s && (i.memoizedState = n, rc = !0), i = i.queue, us(Ho.bind(null, r, i, e), [e]), i.getSnapshot !== t || s || fo !== null && fo.memoizedState.tag & 1) {
			if (r.flags |= 2048, as(9, { destroy: void 0 }, Vo.bind(null, r, i, n, t), null), q === null) throw Error(a(349));
			o || uo & 127 || Bo(r, t, n);
		}
		return n;
	}
	function Bo(e, t, n) {
		e.flags |= 16384, e = {
			getSnapshot: t,
			value: n
		}, t = H.updateQueue, t === null ? (t = jo(), H.updateQueue = t, t.stores = [e]) : (n = t.stores, n === null ? t.stores = [e] : n.push(e));
	}
	function Vo(e, t, n, r) {
		t.value = n, t.getSnapshot = r, Uo(t) && Wo(e);
	}
	function Ho(e, t, n) {
		return n(function() {
			Uo(t) && Wo(e);
		});
	}
	function Uo(e) {
		var t = e.getSnapshot;
		e = e.value;
		try {
			var n = t();
			return !wr(e, n);
		} catch {
			return !0;
		}
	}
	function Wo(e) {
		var t = ai(e, 2);
		t !== null && hu(t, e, 2);
	}
	function Go(e) {
		var t = ko();
		if (typeof e == "function") {
			var n = e;
			if (e = n(), ho) {
				Ve(!0);
				try {
					n();
				} finally {
					Ve(!1);
				}
			}
		}
		return t.memoizedState = t.baseState = e, t.queue = {
			pending: null,
			lanes: 0,
			dispatch: null,
			lastRenderedReducer: Fo,
			lastRenderedState: e
		}, t;
	}
	function Ko(e, t, n, r) {
		return e.baseState = n, Lo(e, U, typeof r == "function" ? r : Fo);
	}
	function qo(e, t, n, r, i) {
		if (Fs(e)) throw Error(a(485));
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
			j.T === null ? o.isTransition = !1 : n(!0), r(o), n = t.pending, n === null ? (o.next = t.pending = o, Jo(t, o)) : (o.next = n.next, t.pending = n.next = o);
		}
	}
	function Jo(e, t) {
		var n = t.action, r = t.payload, i = e.state;
		if (t.isTransition) {
			var a = j.T, o = {};
			j.T = o;
			try {
				var s = n(i, r), c = j.S;
				c !== null && c(o, s), Yo(e, t, s);
			} catch (n) {
				Zo(e, t, n);
			} finally {
				a !== null && o.types !== null && (a.types = o.types), j.T = a;
			}
		} else try {
			a = n(i, r), Yo(e, t, a);
		} catch (n) {
			Zo(e, t, n);
		}
	}
	function Yo(e, t, n) {
		typeof n == "object" && n && typeof n.then == "function" ? n.then(function(n) {
			Xo(e, t, n);
		}, function(n) {
			return Zo(e, t, n);
		}) : Xo(e, t, n);
	}
	function Xo(e, t, n) {
		t.status = "fulfilled", t.value = n, Qo(t), e.state = n, t = e.pending, t !== null && (n = t.next, n === t ? e.pending = null : (n = n.next, t.next = n, Jo(e, n)));
	}
	function Zo(e, t, n) {
		var r = e.pending;
		if (e.pending = null, r !== null) {
			r = r.next;
			do
				t.status = "rejected", t.reason = n, Qo(t), t = t.next;
			while (t !== r);
		}
		e.action = null;
	}
	function Qo(e) {
		e = e.listeners;
		for (var t = 0; t < e.length; t++) (0, e[t])();
	}
	function $o(e, t) {
		return t;
	}
	function es(e, t) {
		if (R) {
			var n = q.formState;
			if (n !== null) {
				a: {
					var r = H;
					if (R) {
						if (L) {
							b: {
								for (var i = L, a = Li; i.nodeType !== 8;) {
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
								L = cf(i.nextSibling), r = i.data === "F!";
								break a;
							}
						}
						zi(r);
					}
					r = !1;
				}
				r && (t = n[0]);
			}
		}
		return n = ko(), n.memoizedState = n.baseState = t, r = {
			pending: null,
			lanes: 0,
			dispatch: null,
			lastRenderedReducer: $o,
			lastRenderedState: t
		}, n.queue = r, n = Ms.bind(null, H, r), r.dispatch = n, r = Go(!1), a = Ps.bind(null, H, !1, r.queue), r = ko(), i = {
			state: t,
			dispatch: null,
			action: e,
			pending: null
		}, r.queue = i, n = qo.bind(null, H, i, a, n), i.dispatch = n, r.memoizedState = e, [
			t,
			n,
			!1
		];
	}
	function ts(e) {
		return ns(Ao(), U, e);
	}
	function ns(e, t, n) {
		if (t = Lo(e, t, $o)[0], e = Io(Fo)[0], typeof t == "object" && t && typeof t.then == "function") try {
			var r = Mo(t);
		} catch (e) {
			throw e === V ? Sa : e;
		}
		else r = t;
		t = Ao();
		var i = t.queue, a = i.dispatch;
		return n !== t.memoizedState && (H.flags |= 2048, as(9, { destroy: void 0 }, rs.bind(null, i, n), null)), [
			r,
			a,
			e
		];
	}
	function rs(e, t) {
		e.action = t;
	}
	function is(e) {
		var t = Ao(), n = U;
		if (n !== null) return ns(t, n, e);
		Ao(), t = t.memoizedState, n = Ao();
		var r = n.queue.dispatch;
		return n.memoizedState = e, [
			t,
			r,
			!1
		];
	}
	function as(e, t, n, r) {
		return e = {
			tag: e,
			create: n,
			deps: r,
			inst: t,
			next: null
		}, t = H.updateQueue, t === null && (t = jo(), H.updateQueue = t), n = t.lastEffect, n === null ? t.lastEffect = e.next = e : (r = n.next, n.next = e, e.next = r, t.lastEffect = e), e;
	}
	function os() {
		return Ao().memoizedState;
	}
	function ss(e, t, n, r) {
		var i = ko();
		H.flags |= e, i.memoizedState = as(1 | t, { destroy: void 0 }, n, r === void 0 ? null : r);
	}
	function cs(e, t, n, r) {
		var i = Ao();
		r = r === void 0 ? null : r;
		var a = i.memoizedState.inst;
		U !== null && r !== null && xo(r, U.memoizedState.deps) ? i.memoizedState = as(t, a, n, r) : (H.flags |= e, i.memoizedState = as(1 | t, a, n, r));
	}
	function ls(e, t) {
		ss(8390656, 8, e, t);
	}
	function us(e, t) {
		cs(2048, 8, e, t);
	}
	function ds(e) {
		H.flags |= 4;
		var t = H.updateQueue;
		if (t === null) t = jo(), H.updateQueue = t, t.events = [e];
		else {
			var n = t.events;
			n === null ? t.events = [e] : n.push(e);
		}
	}
	function fs(e) {
		var t = Ao().memoizedState;
		return ds({
			ref: t,
			nextImpl: e
		}), function() {
			if (K & 2) throw Error(a(440));
			return t.impl.apply(void 0, arguments);
		};
	}
	function ps(e, t) {
		return cs(4, 2, e, t);
	}
	function ms(e, t) {
		return cs(4, 4, e, t);
	}
	function hs(e, t) {
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
	function gs(e, t, n) {
		n = n == null ? null : n.concat([e]), cs(4, 4, hs.bind(null, t, e), n);
	}
	function _s() {}
	function vs(e, t) {
		var n = Ao();
		t = t === void 0 ? null : t;
		var r = n.memoizedState;
		return t !== null && xo(t, r[1]) ? r[0] : (n.memoizedState = [e, t], e);
	}
	function ys(e, t) {
		var n = Ao();
		t = t === void 0 ? null : t;
		var r = n.memoizedState;
		if (t !== null && xo(t, r[1])) return r[0];
		if (r = e(), ho) {
			Ve(!0);
			try {
				e();
			} finally {
				Ve(!1);
			}
		}
		return n.memoizedState = [r, t], r;
	}
	function bs(e, t, n) {
		return n === void 0 || uo & 1073741824 && !(Y & 261930) ? e.memoizedState = t : (e.memoizedState = n, e = mu(), H.lanes |= e, Gl |= e, n);
	}
	function xs(e, t, n, r) {
		return wr(n, t) ? n : Xa.current === null ? !(uo & 42) || uo & 1073741824 && !(Y & 261930) ? (rc = !0, e.memoizedState = n) : (e = mu(), H.lanes |= e, Gl |= e, t) : (e = bs(e, n, r), wr(e, t) || (rc = !0), e);
	}
	function Ss(e, t, n, r, i) {
		var a = M.p;
		M.p = a !== 0 && 8 > a ? a : 8;
		var o = j.T, s = {};
		j.T = s, Ps(e, !1, t, n);
		try {
			var c = i(), l = j.S;
			l !== null && l(s, c), typeof c == "object" && c && typeof c.then == "function" ? Ns(e, t, _a(c, r), pu(e)) : Ns(e, t, r, pu(e));
		} catch (n) {
			Ns(e, t, {
				then: function() {},
				status: "rejected",
				reason: n
			}, pu());
		} finally {
			M.p = a, o !== null && s.types !== null && (o.types = s.types), j.T = o;
		}
	}
	function Cs() {}
	function ws(e, t, n, r) {
		if (e.tag !== 5) throw Error(a(476));
		var i = Ts(e).queue;
		Ss(e, i, t, ae, n === null ? Cs : function() {
			return Es(e), n(r);
		});
	}
	function Ts(e) {
		var t = e.memoizedState;
		if (t !== null) return t;
		t = {
			memoizedState: ae,
			baseState: ae,
			baseQueue: null,
			queue: {
				pending: null,
				lanes: 0,
				dispatch: null,
				lastRenderedReducer: Fo,
				lastRenderedState: ae
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
				lastRenderedReducer: Fo,
				lastRenderedState: n
			},
			next: null
		}, e.memoizedState = t, e = e.alternate, e !== null && (e.memoizedState = t), t;
	}
	function Es(e) {
		var t = Ts(e);
		t.next === null && (t = e.alternate.memoizedState), Ns(e, t.next.queue, {}, pu());
	}
	function Ds() {
		return na(Qf);
	}
	function Os() {
		return Ao().memoizedState;
	}
	function ks() {
		return Ao().memoizedState;
	}
	function As(e) {
		for (var t = e.return; t !== null;) {
			switch (t.tag) {
				case 24:
				case 3:
					var n = pu();
					e = Va(n);
					var r = Ha(t, e, n);
					r !== null && (hu(r, t, n), Ua(r, t, n)), t = { cache: la() }, e.payload = t;
					return;
			}
			t = t.return;
		}
	}
	function js(e, t, n) {
		var r = pu();
		n = {
			lane: r,
			revertLane: 0,
			gesture: null,
			action: n,
			hasEagerState: !1,
			eagerState: null,
			next: null
		}, Fs(e) ? Is(t, n) : (n = ii(e, t, n, r), n !== null && (hu(n, e, r), Ls(n, t, r)));
	}
	function Ms(e, t, n) {
		Ns(e, t, n, pu());
	}
	function Ns(e, t, n, r) {
		var i = {
			lane: r,
			revertLane: 0,
			gesture: null,
			action: n,
			hasEagerState: !1,
			eagerState: null,
			next: null
		};
		if (Fs(e)) Is(t, i);
		else {
			var a = e.alternate;
			if (e.lanes === 0 && (a === null || a.lanes === 0) && (a = t.lastRenderedReducer, a !== null)) try {
				var o = t.lastRenderedState, s = a(o, n);
				if (i.hasEagerState = !0, i.eagerState = s, wr(s, o)) return ri(e, t, i, 0), q === null && ni(), !1;
			} catch {}
			if (n = ii(e, t, i, r), n !== null) return hu(n, e, r), Ls(n, t, r), !0;
		}
		return !1;
	}
	function Ps(e, t, n, r) {
		if (r = {
			lane: 2,
			revertLane: dd(),
			gesture: null,
			action: r,
			hasEagerState: !1,
			eagerState: null,
			next: null
		}, Fs(e)) {
			if (t) throw Error(a(479));
		} else t = ii(e, n, r, 2), t !== null && hu(t, e, 2);
	}
	function Fs(e) {
		var t = e.alternate;
		return e === H || t !== null && t === H;
	}
	function Is(e, t) {
		mo = po = !0;
		var n = e.pending;
		n === null ? t.next = t : (t.next = n.next, n.next = t), e.pending = t;
	}
	function Ls(e, t, n) {
		if (n & 4194048) {
			var r = t.lanes;
			r &= e.pendingLanes, n |= r, t.lanes = n, it(e, n);
		}
	}
	var Rs = {
		readContext: na,
		use: No,
		useCallback: bo,
		useContext: bo,
		useEffect: bo,
		useImperativeHandle: bo,
		useLayoutEffect: bo,
		useInsertionEffect: bo,
		useMemo: bo,
		useReducer: bo,
		useRef: bo,
		useState: bo,
		useDebugValue: bo,
		useDeferredValue: bo,
		useTransition: bo,
		useSyncExternalStore: bo,
		useId: bo,
		useHostTransitionStatus: bo,
		useFormState: bo,
		useActionState: bo,
		useOptimistic: bo,
		useMemoCache: bo,
		useCacheRefresh: bo
	};
	Rs.useEffectEvent = bo;
	var zs = {
		readContext: na,
		use: No,
		useCallback: function(e, t) {
			return ko().memoizedState = [e, t === void 0 ? null : t], e;
		},
		useContext: na,
		useEffect: ls,
		useImperativeHandle: function(e, t, n) {
			n = n == null ? null : n.concat([e]), ss(4194308, 4, hs.bind(null, t, e), n);
		},
		useLayoutEffect: function(e, t) {
			return ss(4194308, 4, e, t);
		},
		useInsertionEffect: function(e, t) {
			ss(4, 2, e, t);
		},
		useMemo: function(e, t) {
			var n = ko();
			t = t === void 0 ? null : t;
			var r = e();
			if (ho) {
				Ve(!0);
				try {
					e();
				} finally {
					Ve(!1);
				}
			}
			return n.memoizedState = [r, t], r;
		},
		useReducer: function(e, t, n) {
			var r = ko();
			if (n !== void 0) {
				var i = n(t);
				if (ho) {
					Ve(!0);
					try {
						n(t);
					} finally {
						Ve(!1);
					}
				}
			} else i = t;
			return r.memoizedState = r.baseState = i, e = {
				pending: null,
				lanes: 0,
				dispatch: null,
				lastRenderedReducer: e,
				lastRenderedState: i
			}, r.queue = e, e = e.dispatch = js.bind(null, H, e), [r.memoizedState, e];
		},
		useRef: function(e) {
			var t = ko();
			return e = { current: e }, t.memoizedState = e;
		},
		useState: function(e) {
			e = Go(e);
			var t = e.queue, n = Ms.bind(null, H, t);
			return t.dispatch = n, [e.memoizedState, n];
		},
		useDebugValue: _s,
		useDeferredValue: function(e, t) {
			return bs(ko(), e, t);
		},
		useTransition: function() {
			var e = Go(!1);
			return e = Ss.bind(null, H, e.queue, !0, !1), ko().memoizedState = e, [!1, e];
		},
		useSyncExternalStore: function(e, t, n) {
			var r = H, i = ko();
			if (R) {
				if (n === void 0) throw Error(a(407));
				n = n();
			} else {
				if (n = t(), q === null) throw Error(a(349));
				Y & 127 || Bo(r, t, n);
			}
			i.memoizedState = n;
			var o = {
				value: n,
				getSnapshot: t
			};
			return i.queue = o, ls(Ho.bind(null, r, o, e), [e]), r.flags |= 2048, as(9, { destroy: void 0 }, Vo.bind(null, r, o, n, t), null), n;
		},
		useId: function() {
			var e = ko(), t = q.identifierPrefix;
			if (R) {
				var n = ki, r = Oi;
				n = (r & ~(1 << 32 - He(r) - 1)).toString(32) + n, t = "_" + t + "R_" + n, n = go++, 0 < n && (t += "H" + n.toString(32)), t += "_";
			} else n = yo++, t = "_" + t + "r_" + n.toString(32) + "_";
			return e.memoizedState = t;
		},
		useHostTransitionStatus: Ds,
		useFormState: es,
		useActionState: es,
		useOptimistic: function(e) {
			var t = ko();
			t.memoizedState = t.baseState = e;
			var n = {
				pending: null,
				lanes: 0,
				dispatch: null,
				lastRenderedReducer: null,
				lastRenderedState: null
			};
			return t.queue = n, t = Ps.bind(null, H, !0, n), n.dispatch = t, [e, t];
		},
		useMemoCache: Po,
		useCacheRefresh: function() {
			return ko().memoizedState = As.bind(null, H);
		},
		useEffectEvent: function(e) {
			var t = ko(), n = { impl: e };
			return t.memoizedState = n, function() {
				if (K & 2) throw Error(a(440));
				return n.impl.apply(void 0, arguments);
			};
		}
	}, Bs = {
		readContext: na,
		use: No,
		useCallback: vs,
		useContext: na,
		useEffect: us,
		useImperativeHandle: gs,
		useInsertionEffect: ps,
		useLayoutEffect: ms,
		useMemo: ys,
		useReducer: Io,
		useRef: os,
		useState: function() {
			return Io(Fo);
		},
		useDebugValue: _s,
		useDeferredValue: function(e, t) {
			return xs(Ao(), U.memoizedState, e, t);
		},
		useTransition: function() {
			var e = Io(Fo)[0], t = Ao().memoizedState;
			return [typeof e == "boolean" ? e : Mo(e), t];
		},
		useSyncExternalStore: zo,
		useId: Os,
		useHostTransitionStatus: Ds,
		useFormState: ts,
		useActionState: ts,
		useOptimistic: function(e, t) {
			return Ko(Ao(), U, e, t);
		},
		useMemoCache: Po,
		useCacheRefresh: ks
	};
	Bs.useEffectEvent = fs;
	var Vs = {
		readContext: na,
		use: No,
		useCallback: vs,
		useContext: na,
		useEffect: us,
		useImperativeHandle: gs,
		useInsertionEffect: ps,
		useLayoutEffect: ms,
		useMemo: ys,
		useReducer: Ro,
		useRef: os,
		useState: function() {
			return Ro(Fo);
		},
		useDebugValue: _s,
		useDeferredValue: function(e, t) {
			var n = Ao();
			return U === null ? bs(n, e, t) : xs(n, U.memoizedState, e, t);
		},
		useTransition: function() {
			var e = Ro(Fo)[0], t = Ao().memoizedState;
			return [typeof e == "boolean" ? e : Mo(e), t];
		},
		useSyncExternalStore: zo,
		useId: Os,
		useHostTransitionStatus: Ds,
		useFormState: is,
		useActionState: is,
		useOptimistic: function(e, t) {
			var n = Ao();
			return U === null ? (n.baseState = e, [e, n.queue.dispatch]) : Ko(n, U, e, t);
		},
		useMemoCache: Po,
		useCacheRefresh: ks
	};
	Vs.useEffectEvent = fs;
	function Hs(e, t, n, r) {
		t = e.memoizedState, n = n(r, t), n = n == null ? t : h({}, t, n), e.memoizedState = n, e.lanes === 0 && (e.updateQueue.baseState = n);
	}
	var Us = {
		enqueueSetState: function(e, t, n) {
			e = e._reactInternals;
			var r = pu(), i = Va(r);
			i.payload = t, n != null && (i.callback = n), t = Ha(e, i, r), t !== null && (hu(t, e, r), Ua(t, e, r));
		},
		enqueueReplaceState: function(e, t, n) {
			e = e._reactInternals;
			var r = pu(), i = Va(r);
			i.tag = 1, i.payload = t, n != null && (i.callback = n), t = Ha(e, i, r), t !== null && (hu(t, e, r), Ua(t, e, r));
		},
		enqueueForceUpdate: function(e, t) {
			e = e._reactInternals;
			var n = pu(), r = Va(n);
			r.tag = 2, t != null && (r.callback = t), t = Ha(e, r, n), t !== null && (hu(t, e, n), Ua(t, e, n));
		}
	};
	function Ws(e, t, n, r, i, a, o) {
		return e = e.stateNode, typeof e.shouldComponentUpdate == "function" ? e.shouldComponentUpdate(r, a, o) : t.prototype && t.prototype.isPureReactComponent ? !Tr(n, r) || !Tr(i, a) : !0;
	}
	function Gs(e, t, n, r) {
		e = t.state, typeof t.componentWillReceiveProps == "function" && t.componentWillReceiveProps(n, r), typeof t.UNSAFE_componentWillReceiveProps == "function" && t.UNSAFE_componentWillReceiveProps(n, r), t.state !== e && Us.enqueueReplaceState(t, t.state, null);
	}
	function Ks(e, t) {
		var n = t;
		if ("ref" in t) for (var r in n = {}, t) r !== "ref" && (n[r] = t[r]);
		if (e = e.defaultProps) for (var i in n === t && (n = h({}, n)), e) n[i] === void 0 && (n[i] = e[i]);
		return n;
	}
	function qs(e) {
		Qr(e);
	}
	function Js(e) {
		console.error(e);
	}
	function Ys(e) {
		Qr(e);
	}
	function Xs(e, t) {
		try {
			var n = e.onUncaughtError;
			n(t.value, { componentStack: t.stack });
		} catch (e) {
			setTimeout(function() {
				throw e;
			});
		}
	}
	function Zs(e, t, n) {
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
	function Qs(e, t, n) {
		return n = Va(n), n.tag = 3, n.payload = { element: null }, n.callback = function() {
			Xs(e, t);
		}, n;
	}
	function $s(e) {
		return e = Va(e), e.tag = 3, e;
	}
	function ec(e, t, n, r) {
		var i = n.type.getDerivedStateFromError;
		if (typeof i == "function") {
			var a = r.value;
			e.payload = function() {
				return i(a);
			}, e.callback = function() {
				Zs(t, n, r);
			};
		}
		var o = n.stateNode;
		o !== null && typeof o.componentDidCatch == "function" && (e.callback = function() {
			Zs(t, n, r), typeof i != "function" && (ru === null ? ru = /* @__PURE__ */ new Set([this]) : ru.add(this));
			var e = r.stack;
			this.componentDidCatch(r.value, { componentStack: e === null ? "" : e });
		});
	}
	function tc(e, t, n, r, i) {
		if (n.flags |= 32768, typeof r == "object" && r && typeof r.then == "function") {
			if (t = n.alternate, t !== null && $i(t, n, i, !0), n = to.current, n !== null) {
				switch (n.tag) {
					case 31:
					case 13: return no === null ? Du() : n.alternate === null && Wl === 0 && (Wl = 3), n.flags &= -257, n.flags |= 65536, n.lanes = i, r === Ca ? n.flags |= 16384 : (t = n.updateQueue, t === null ? n.updateQueue = /* @__PURE__ */ new Set([r]) : t.add(r), Gu(e, r, i)), !1;
					case 22: return n.flags |= 65536, r === Ca ? n.flags |= 16384 : (t = n.updateQueue, t === null ? (t = {
						transitions: null,
						markerInstances: null,
						retryQueue: /* @__PURE__ */ new Set([r])
					}, n.updateQueue = t) : (n = t.retryQueue, n === null ? t.retryQueue = /* @__PURE__ */ new Set([r]) : n.add(r)), Gu(e, r, i)), !1;
				}
				throw Error(a(435, n.tag));
			}
			return Gu(e, r, i), Du(), !1;
		}
		if (R) return t = to.current, t === null ? (r !== Ri && (t = Error(a(423), { cause: r }), Gi(bi(t, n))), e = e.current.alternate, e.flags |= 65536, i &= -i, e.lanes |= i, r = bi(r, n), i = Qs(e.stateNode, r, i), Wa(e, i), Wl !== 4 && (Wl = 2)) : (!(t.flags & 65536) && (t.flags |= 256), t.flags |= 65536, t.lanes = i, r !== Ri && (e = Error(a(422), { cause: r }), Gi(bi(e, n)))), !1;
		var o = Error(a(520), { cause: r });
		if (o = bi(o, n), Xl === null ? Xl = [o] : Xl.push(o), Wl !== 4 && (Wl = 2), t === null) return !0;
		r = bi(r, n), n = t;
		do {
			switch (n.tag) {
				case 3: return n.flags |= 65536, e = i & -i, n.lanes |= e, e = Qs(n.stateNode, r, e), Wa(n, e), !1;
				case 1: if (t = n.type, o = n.stateNode, !(n.flags & 128) && (typeof t.getDerivedStateFromError == "function" || o !== null && typeof o.componentDidCatch == "function" && (ru === null || !ru.has(o)))) return n.flags |= 65536, i &= -i, n.lanes |= i, i = $s(i), ec(i, e, n, r), Wa(n, i), !1;
			}
			n = n.return;
		} while (n !== null);
		return !1;
	}
	var nc = Error(a(461)), rc = !1;
	function ic(e, t, n, r) {
		t.child = e === null ? La(t, null, n, r) : Ia(t, e.child, n, r);
	}
	function ac(e, t, n, r, i) {
		n = n.render;
		var a = t.ref;
		if ("ref" in r) {
			var o = {};
			for (var s in r) s !== "ref" && (o[s] = r[s]);
		} else o = r;
		return ta(t), r = So(e, t, n, o, a, i), s = Eo(), e !== null && !rc ? (Do(e, t, i), kc(e, t, i)) : (R && s && Mi(t), t.flags |= 1, ic(e, t, r, i), t.child);
	}
	function oc(e, t, n, r, i) {
		if (e === null) {
			var a = n.type;
			return typeof a == "function" && !di(a) && a.defaultProps === void 0 && n.compare === null ? (t.tag = 15, t.type = a, sc(e, t, a, r, i)) : (e = mi(n.type, null, r, t, t.mode, i), e.ref = t.ref, e.return = t, t.child = e);
		}
		if (a = e.child, !Ac(e, i)) {
			var o = a.memoizedProps;
			if (n = n.compare, n = n === null ? Tr : n, n(o, r) && e.ref === t.ref) return kc(e, t, i);
		}
		return t.flags |= 1, e = fi(a, r), e.ref = t.ref, e.return = t, t.child = e;
	}
	function sc(e, t, n, r, i) {
		if (e !== null) {
			var a = e.memoizedProps;
			if (Tr(a, r) && e.ref === t.ref) {
				if (rc = !1, t.pendingProps = r = a, Ac(e, i)) e.flags & 131072 && (rc = !0);
				else return t.lanes = e.lanes, kc(e, t, i);
			}
		}
		return hc(e, t, n, r, i);
	}
	function cc(e, t, n, r) {
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
				return uc(e, t, a, n, r);
			}
			if (n & 536870912) t.memoizedState = {
				baseLanes: 0,
				cachePool: null
			}, e !== null && ba(t, a === null ? null : a.cachePool), a === null ? $a() : Qa(t, a), ao(t);
			else return r = t.lanes = 536870912, uc(e, t, a === null ? n : a.baseLanes | n, n, r);
		} else a === null ? (e !== null && ba(t, null), $a(), oo(t)) : (ba(t, a.cachePool), Qa(t, a), oo(t), t.memoizedState = null);
		return ic(e, t, i, n), t.child;
	}
	function lc(e, t) {
		return e !== null && e.tag === 22 || t.stateNode !== null || (t.stateNode = {
			_visibility: 1,
			_pendingMarkers: null,
			_retryCache: null,
			_transitions: null
		}), t.sibling;
	}
	function uc(e, t, n, r, i) {
		var a = ya();
		return a = a === null ? null : {
			parent: ca._currentValue,
			pool: a
		}, t.memoizedState = {
			baseLanes: n,
			cachePool: a
		}, e !== null && ba(t, null), $a(), ao(t), e !== null && $i(e, t, r, !0), t.childLanes = i, null;
	}
	function dc(e, t) {
		return t = wc({
			mode: t.mode,
			children: t.children
		}, e.mode), t.ref = e.ref, e.child = t, t.return = e, t;
	}
	function fc(e, t, n) {
		return Ia(t, e.child, null, n), e = dc(t, t.pendingProps), e.flags |= 2, so(t), t.memoizedState = null, e;
	}
	function pc(e, t, n) {
		var r = t.pendingProps, i = !!(t.flags & 128);
		if (t.flags &= -129, e === null) {
			if (R) {
				if (r.mode === "hidden") return e = dc(t, r), t.lanes = 536870912, lc(null, e);
				if (io(t), (e = L) ? (e = rf(e, Li), e = e !== null && e.data === "&" ? e : null, e !== null && (t.memoizedState = {
					dehydrated: e,
					treeContext: Di === null ? null : {
						id: Oi,
						overflow: ki
					},
					retryLane: 536870912,
					hydrationErrors: null
				}, n = _i(e), n.return = t, t.child = n, Fi = t, L = null)) : e = null, e === null) throw zi(t);
				return t.lanes = 536870912, null;
			}
			return dc(t, r);
		}
		var o = e.memoizedState;
		if (o !== null) {
			var s = o.dehydrated;
			if (io(t), i) {
				if (t.flags & 256) t.flags &= -257, t = fc(e, t, n);
				else if (t.memoizedState !== null) t.child = e.child, t.flags |= 128, t = null;
				else throw Error(a(558));
			} else if (rc || $i(e, t, n, !1), i = (n & e.childLanes) !== 0, rc || i) {
				if (r = q, r !== null && (s = at(r, n), s !== 0 && s !== o.retryLane)) throw o.retryLane = s, ai(e, s), hu(r, e, s), nc;
				Du(), t = fc(e, t, n);
			} else e = o.treeContext, L = cf(s.nextSibling), Fi = t, R = !0, Ii = null, Li = !1, e !== null && Pi(t, e), t = dc(t, r), t.flags |= 4096;
			return t;
		}
		return e = fi(e.child, {
			mode: r.mode,
			children: r.children
		}), e.ref = t.ref, t.child = e, e.return = t, e;
	}
	function mc(e, t) {
		var n = t.ref;
		if (n === null) e !== null && e.ref !== null && (t.flags |= 4194816);
		else {
			if (typeof n != "function" && typeof n != "object") throw Error(a(284));
			(e === null || e.ref !== n) && (t.flags |= 4194816);
		}
	}
	function hc(e, t, n, r, i) {
		return ta(t), n = So(e, t, n, r, void 0, i), r = Eo(), e !== null && !rc ? (Do(e, t, i), kc(e, t, i)) : (R && r && Mi(t), t.flags |= 1, ic(e, t, n, i), t.child);
	}
	function gc(e, t, n, r, i, a) {
		return ta(t), t.updateQueue = null, n = wo(t, r, n, i), Co(e), r = Eo(), e !== null && !rc ? (Do(e, t, a), kc(e, t, a)) : (R && r && Mi(t), t.flags |= 1, ic(e, t, n, a), t.child);
	}
	function _c(e, t, n, r, i) {
		if (ta(t), t.stateNode === null) {
			var a = ci, o = n.contextType;
			typeof o == "object" && o && (a = na(o)), a = new n(r, a), t.memoizedState = a.state !== null && a.state !== void 0 ? a.state : null, a.updater = Us, t.stateNode = a, a._reactInternals = t, a = t.stateNode, a.props = r, a.state = t.memoizedState, a.refs = {}, za(t), o = n.contextType, a.context = typeof o == "object" && o ? na(o) : ci, a.state = t.memoizedState, o = n.getDerivedStateFromProps, typeof o == "function" && (Hs(t, n, o, r), a.state = t.memoizedState), typeof n.getDerivedStateFromProps == "function" || typeof a.getSnapshotBeforeUpdate == "function" || typeof a.UNSAFE_componentWillMount != "function" && typeof a.componentWillMount != "function" || (o = a.state, typeof a.componentWillMount == "function" && a.componentWillMount(), typeof a.UNSAFE_componentWillMount == "function" && a.UNSAFE_componentWillMount(), o !== a.state && Us.enqueueReplaceState(a, a.state, null), qa(t, r, a, i), Ka(), a.state = t.memoizedState), typeof a.componentDidMount == "function" && (t.flags |= 4194308), r = !0;
		} else if (e === null) {
			a = t.stateNode;
			var s = t.memoizedProps, c = Ks(n, s);
			a.props = c;
			var l = a.context, u = n.contextType;
			o = ci, typeof u == "object" && u && (o = na(u));
			var d = n.getDerivedStateFromProps;
			u = typeof d == "function" || typeof a.getSnapshotBeforeUpdate == "function", s = t.pendingProps !== s, u || typeof a.UNSAFE_componentWillReceiveProps != "function" && typeof a.componentWillReceiveProps != "function" || (s || l !== o) && Gs(t, a, r, o), Ra = !1;
			var f = t.memoizedState;
			a.state = f, qa(t, r, a, i), Ka(), l = t.memoizedState, s || f !== l || Ra ? (typeof d == "function" && (Hs(t, n, d, r), l = t.memoizedState), (c = Ra || Ws(t, n, c, r, f, l, o)) ? (u || typeof a.UNSAFE_componentWillMount != "function" && typeof a.componentWillMount != "function" || (typeof a.componentWillMount == "function" && a.componentWillMount(), typeof a.UNSAFE_componentWillMount == "function" && a.UNSAFE_componentWillMount()), typeof a.componentDidMount == "function" && (t.flags |= 4194308)) : (typeof a.componentDidMount == "function" && (t.flags |= 4194308), t.memoizedProps = r, t.memoizedState = l), a.props = r, a.state = l, a.context = o, r = c) : (typeof a.componentDidMount == "function" && (t.flags |= 4194308), r = !1);
		} else {
			a = t.stateNode, Ba(e, t), o = t.memoizedProps, u = Ks(n, o), a.props = u, d = t.pendingProps, f = a.context, l = n.contextType, c = ci, typeof l == "object" && l && (c = na(l)), s = n.getDerivedStateFromProps, (l = typeof s == "function" || typeof a.getSnapshotBeforeUpdate == "function") || typeof a.UNSAFE_componentWillReceiveProps != "function" && typeof a.componentWillReceiveProps != "function" || (o !== d || f !== c) && Gs(t, a, r, c), Ra = !1, f = t.memoizedState, a.state = f, qa(t, r, a, i), Ka();
			var p = t.memoizedState;
			o !== d || f !== p || Ra || e !== null && e.dependencies !== null && ea(e.dependencies) ? (typeof s == "function" && (Hs(t, n, s, r), p = t.memoizedState), (u = Ra || Ws(t, n, u, r, f, p, c) || e !== null && e.dependencies !== null && ea(e.dependencies)) ? (l || typeof a.UNSAFE_componentWillUpdate != "function" && typeof a.componentWillUpdate != "function" || (typeof a.componentWillUpdate == "function" && a.componentWillUpdate(r, p, c), typeof a.UNSAFE_componentWillUpdate == "function" && a.UNSAFE_componentWillUpdate(r, p, c)), typeof a.componentDidUpdate == "function" && (t.flags |= 4), typeof a.getSnapshotBeforeUpdate == "function" && (t.flags |= 1024)) : (typeof a.componentDidUpdate != "function" || o === e.memoizedProps && f === e.memoizedState || (t.flags |= 4), typeof a.getSnapshotBeforeUpdate != "function" || o === e.memoizedProps && f === e.memoizedState || (t.flags |= 1024), t.memoizedProps = r, t.memoizedState = p), a.props = r, a.state = p, a.context = c, r = u) : (typeof a.componentDidUpdate != "function" || o === e.memoizedProps && f === e.memoizedState || (t.flags |= 4), typeof a.getSnapshotBeforeUpdate != "function" || o === e.memoizedProps && f === e.memoizedState || (t.flags |= 1024), r = !1);
		}
		return a = r, mc(e, t), r = !!(t.flags & 128), a || r ? (a = t.stateNode, n = r && typeof n.getDerivedStateFromError != "function" ? null : a.render(), t.flags |= 1, e !== null && r ? (t.child = Ia(t, e.child, null, i), t.child = Ia(t, null, n, i)) : ic(e, t, n, i), t.memoizedState = a.state, e = t.child) : e = kc(e, t, i), e;
	}
	function vc(e, t, n, r) {
		return Ui(), t.flags |= 256, ic(e, t, n, r), t.child;
	}
	var yc = {
		dehydrated: null,
		treeContext: null,
		retryLane: 0,
		hydrationErrors: null
	};
	function bc(e) {
		return {
			baseLanes: e,
			cachePool: B()
		};
	}
	function xc(e, t, n) {
		return e = e === null ? 0 : e.childLanes & ~n, t && (e |= Jl), e;
	}
	function Sc(e, t, n) {
		var r = t.pendingProps, i = !1, o = !!(t.flags & 128), s;
		if ((s = o) || (s = e !== null && e.memoizedState === null ? !1 : !!(co.current & 2)), s && (i = !0, t.flags &= -129), s = !!(t.flags & 32), t.flags &= -33, e === null) {
			if (R) {
				if (i ? ro(t) : oo(t), (e = L) ? (e = rf(e, Li), e = e !== null && e.data !== "&" ? e : null, e !== null && (t.memoizedState = {
					dehydrated: e,
					treeContext: Di === null ? null : {
						id: Oi,
						overflow: ki
					},
					retryLane: 536870912,
					hydrationErrors: null
				}, n = _i(e), n.return = t, t.child = n, Fi = t, L = null)) : e = null, e === null) throw zi(t);
				return of(e) ? t.lanes = 32 : t.lanes = 536870912, null;
			}
			var c = r.children;
			return r = r.fallback, i ? (oo(t), i = t.mode, c = wc({
				mode: "hidden",
				children: c
			}, i), r = hi(r, i, n, null), c.return = t, r.return = t, c.sibling = r, t.child = c, r = t.child, r.memoizedState = bc(n), r.childLanes = xc(e, s, n), t.memoizedState = yc, lc(null, r)) : (ro(t), Cc(t, c));
		}
		var l = e.memoizedState;
		if (l !== null && (c = l.dehydrated, c !== null)) {
			if (o) t.flags & 256 ? (ro(t), t.flags &= -257, t = Tc(e, t, n)) : t.memoizedState === null ? (oo(t), c = r.fallback, i = t.mode, r = wc({
				mode: "visible",
				children: r.children
			}, i), c = hi(c, i, n, null), c.flags |= 2, r.return = t, c.return = t, r.sibling = c, t.child = r, Ia(t, e.child, null, n), r = t.child, r.memoizedState = bc(n), r.childLanes = xc(e, s, n), t.memoizedState = yc, t = lc(null, r)) : (oo(t), t.child = e.child, t.flags |= 128, t = null);
			else if (ro(t), of(c)) {
				if (s = c.nextSibling && c.nextSibling.dataset, s) var u = s.dgst;
				s = u, r = Error(a(419)), r.stack = "", r.digest = s, Gi({
					value: r,
					source: null,
					stack: null
				}), t = Tc(e, t, n);
			} else if (rc || $i(e, t, n, !1), s = (n & e.childLanes) !== 0, rc || s) {
				if (s = q, s !== null && (r = at(s, n), r !== 0 && r !== l.retryLane)) throw l.retryLane = r, ai(e, r), hu(s, e, r), nc;
				af(c) || Du(), t = Tc(e, t, n);
			} else af(c) ? (t.flags |= 192, t.child = e.child, t = null) : (e = l.treeContext, L = cf(c.nextSibling), Fi = t, R = !0, Ii = null, Li = !1, e !== null && Pi(t, e), t = Cc(t, r.children), t.flags |= 4096);
			return t;
		}
		return i ? (oo(t), c = r.fallback, i = t.mode, l = e.child, u = l.sibling, r = fi(l, {
			mode: "hidden",
			children: r.children
		}), r.subtreeFlags = l.subtreeFlags & 65011712, u === null ? (c = hi(c, i, n, null), c.flags |= 2) : c = fi(u, c), c.return = t, r.return = t, r.sibling = c, t.child = r, lc(null, r), r = t.child, c = e.child.memoizedState, c === null ? c = bc(n) : (i = c.cachePool, i === null ? i = B() : (l = ca._currentValue, i = i.parent === l ? i : {
			parent: l,
			pool: l
		}), c = {
			baseLanes: c.baseLanes | n,
			cachePool: i
		}), r.memoizedState = c, r.childLanes = xc(e, s, n), t.memoizedState = yc, lc(e.child, r)) : (ro(t), n = e.child, e = n.sibling, n = fi(n, {
			mode: "visible",
			children: r.children
		}), n.return = t, n.sibling = null, e !== null && (s = t.deletions, s === null ? (t.deletions = [e], t.flags |= 16) : s.push(e)), t.child = n, t.memoizedState = null, n);
	}
	function Cc(e, t) {
		return t = wc({
			mode: "visible",
			children: t
		}, e.mode), t.return = e, e.child = t;
	}
	function wc(e, t) {
		return e = ui(22, e, null, t), e.lanes = 0, e;
	}
	function Tc(e, t, n) {
		return Ia(t, e.child, null, n), e = Cc(t, t.pendingProps.children), e.flags |= 2, t.memoizedState = null, e;
	}
	function Ec(e, t, n) {
		e.lanes |= t;
		var r = e.alternate;
		r !== null && (r.lanes |= t), Zi(e.return, t, n);
	}
	function Dc(e, t, n, r, i, a) {
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
	function Oc(e, t, n) {
		var r = t.pendingProps, i = r.revealOrder, a = r.tail;
		r = r.children;
		var o = co.current, s = !!(o & 2);
		if (s ? (o = o & 1 | 2, t.flags |= 128) : o &= 1, N(co, o), ic(e, t, r, n), r = R ? wi : 0, !s && e !== null && e.flags & 128) a: for (e = t.child; e !== null;) {
			if (e.tag === 13) e.memoizedState !== null && Ec(e, n, t);
			else if (e.tag === 19) Ec(e, n, t);
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
				for (n = t.child, i = null; n !== null;) e = n.alternate, e !== null && lo(e) === null && (i = n), n = n.sibling;
				n = i, n === null ? (i = t.child, t.child = null) : (i = n.sibling, n.sibling = null), Dc(t, !1, i, n, a, r);
				break;
			case "backwards":
			case "unstable_legacy-backwards":
				for (n = null, i = t.child, t.child = null; i !== null;) {
					if (e = i.alternate, e !== null && lo(e) === null) {
						t.child = i;
						break;
					}
					e = i.sibling, i.sibling = n, n = i, i = e;
				}
				Dc(t, !0, n, null, a, r);
				break;
			case "together":
				Dc(t, !1, null, null, void 0, r);
				break;
			default: t.memoizedState = null;
		}
		return t.child;
	}
	function kc(e, t, n) {
		if (e !== null && (t.dependencies = e.dependencies), Gl |= t.lanes, (n & t.childLanes) === 0) {
			if (e !== null) {
				if ($i(e, t, n, !1), (n & t.childLanes) === 0) return null;
			} else return null;
		}
		if (e !== null && t.child !== e.child) throw Error(a(153));
		if (t.child !== null) {
			for (e = t.child, n = fi(e, e.pendingProps), t.child = n, n.return = t; e.sibling !== null;) e = e.sibling, n = n.sibling = fi(e, e.pendingProps), n.return = t;
			n.sibling = null;
		}
		return t.child;
	}
	function Ac(e, t) {
		return (e.lanes & t) !== 0 || (e = e.dependencies, !!(e !== null && ea(e)));
	}
	function jc(e, t, n) {
		switch (t.tag) {
			case 3:
				me(t, t.stateNode.containerInfo), Yi(t, ca, e.memoizedState.cache), Ui();
				break;
			case 27:
			case 5:
				ge(t);
				break;
			case 4:
				me(t, t.stateNode.containerInfo);
				break;
			case 10:
				Yi(t, t.type, t.memoizedProps.value);
				break;
			case 31:
				if (t.memoizedState !== null) return t.flags |= 128, io(t), null;
				break;
			case 13:
				var r = t.memoizedState;
				if (r !== null) return r.dehydrated === null ? (n & t.child.childLanes) === 0 ? (ro(t), e = kc(e, t, n), e === null ? null : e.sibling) : Sc(e, t, n) : (ro(t), t.flags |= 128, null);
				ro(t);
				break;
			case 19:
				var i = !!(e.flags & 128);
				if (r = (n & t.childLanes) !== 0, r ||= ($i(e, t, n, !1), (n & t.childLanes) !== 0), i) {
					if (r) return Oc(e, t, n);
					t.flags |= 128;
				}
				if (i = t.memoizedState, i !== null && (i.rendering = null, i.tail = null, i.lastEffect = null), N(co, co.current), r) break;
				return null;
			case 22: return t.lanes = 0, cc(e, t, n, t.pendingProps);
			case 24: Yi(t, ca, e.memoizedState.cache);
		}
		return kc(e, t, n);
	}
	function Mc(e, t, n) {
		if (e !== null) {
			if (e.memoizedProps !== t.pendingProps) rc = !0;
			else {
				if (!Ac(e, n) && !(t.flags & 128)) return rc = !1, jc(e, t, n);
				rc = !!(e.flags & 131072);
			}
		} else rc = !1, R && t.flags & 1048576 && ji(t, wi, t.index);
		switch (t.lanes = 0, t.tag) {
			case 16:
				a: {
					var r = t.pendingProps;
					if (e = Ea(t.elementType), t.type = e, typeof e == "function") di(e) ? (r = Ks(e, r), t.tag = 1, t = _c(null, t, e, r, n)) : (t.tag = 0, t = hc(null, t, e, r, n));
					else {
						if (e != null) {
							var i = e.$$typeof;
							if (i === w) {
								t.tag = 11, t = ac(null, t, e, r, n);
								break a;
							}
							if (i === ee) {
								t.tag = 14, t = oc(null, t, e, r, n);
								break a;
							}
						}
						throw t = re(e) || e, Error(a(306, t, ""));
					}
				}
				return t;
			case 0: return hc(e, t, t.type, t.pendingProps, n);
			case 1: return r = t.type, i = Ks(r, t.pendingProps), _c(e, t, r, i, n);
			case 3:
				a: {
					if (me(t, t.stateNode.containerInfo), e === null) throw Error(a(387));
					r = t.pendingProps;
					var o = t.memoizedState;
					i = o.element, Ba(e, t), qa(t, r, null, n);
					var s = t.memoizedState;
					if (r = s.cache, Yi(t, ca, r), r !== o.cache && Qi(t, [ca], n, !0), Ka(), r = s.element, o.isDehydrated) {
						if (o = {
							element: r,
							isDehydrated: !1,
							cache: s.cache
						}, t.updateQueue.baseState = o, t.memoizedState = o, t.flags & 256) {
							t = vc(e, t, r, n);
							break a;
						}
						if (r !== i) {
							i = bi(Error(a(424)), t), Gi(i), t = vc(e, t, r, n);
							break a;
						}
						switch (e = t.stateNode.containerInfo, e.nodeType) {
							case 9:
								e = e.body;
								break;
							default: e = e.nodeName === "HTML" ? e.ownerDocument.body : e;
						}
						for (L = cf(e.firstChild), Fi = t, R = !0, Ii = null, Li = !0, n = La(t, null, r, n), t.child = n; n;) n.flags = n.flags & -3 | 4096, n = n.sibling;
					} else {
						if (Ui(), r === i) {
							t = kc(e, t, n);
							break a;
						}
						ic(e, t, r, n);
					}
					t = t.child;
				}
				return t;
			case 26: return mc(e, t), e === null ? (n = kf(t.type, null, t.pendingProps, null)) ? t.memoizedState = n : R || (n = t.type, e = t.pendingProps, r = Bd(fe.current).createElement(n), r[dt] = t, r[ft] = e, Pd(r, n, e), P(r), t.stateNode = r) : t.memoizedState = kf(t.type, e.memoizedProps, t.pendingProps, e.memoizedState), null;
			case 27: return ge(t), e === null && R && (r = t.stateNode = ff(t.type, t.pendingProps, fe.current), Fi = t, Li = !0, i = L, Zd(t.type) ? (lf = i, L = cf(r.firstChild)) : L = i), ic(e, t, t.pendingProps.children, n), mc(e, t), e === null && (t.flags |= 4194304), t.child;
			case 5: return e === null && R && ((i = r = L) && (r = tf(r, t.type, t.pendingProps, Li), r === null ? i = !1 : (t.stateNode = r, Fi = t, L = cf(r.firstChild), Li = !1, i = !0)), i || zi(t)), ge(t), i = t.type, o = t.pendingProps, s = e === null ? null : e.memoizedProps, r = o.children, Ud(i, o) ? r = null : s !== null && Ud(i, s) && (t.flags |= 32), t.memoizedState !== null && (i = So(e, t, To, null, null, n), Qf._currentValue = i), mc(e, t), ic(e, t, r, n), t.child;
			case 6: return e === null && R && ((e = n = L) && (n = nf(n, t.pendingProps, Li), n === null ? e = !1 : (t.stateNode = n, Fi = t, L = null, e = !0)), e || zi(t)), null;
			case 13: return Sc(e, t, n);
			case 4: return me(t, t.stateNode.containerInfo), r = t.pendingProps, e === null ? t.child = Ia(t, null, r, n) : ic(e, t, r, n), t.child;
			case 11: return ac(e, t, t.type, t.pendingProps, n);
			case 7: return ic(e, t, t.pendingProps, n), t.child;
			case 8: return ic(e, t, t.pendingProps.children, n), t.child;
			case 12: return ic(e, t, t.pendingProps.children, n), t.child;
			case 10: return r = t.pendingProps, Yi(t, t.type, r.value), ic(e, t, r.children, n), t.child;
			case 9: return i = t.type._context, r = t.pendingProps.children, ta(t), i = na(i), r = r(i), t.flags |= 1, ic(e, t, r, n), t.child;
			case 14: return oc(e, t, t.type, t.pendingProps, n);
			case 15: return sc(e, t, t.type, t.pendingProps, n);
			case 19: return Oc(e, t, n);
			case 31: return pc(e, t, n);
			case 22: return cc(e, t, n, t.pendingProps);
			case 24: return ta(t), r = na(ca), e === null ? (i = ya(), i === null && (i = q, o = la(), i.pooledCache = o, o.refCount++, o !== null && (i.pooledCacheLanes |= n), i = o), t.memoizedState = {
				parent: r,
				cache: i
			}, za(t), Yi(t, ca, i)) : ((e.lanes & n) !== 0 && (Ba(e, t), qa(t, null, null, n), Ka()), i = e.memoizedState, o = t.memoizedState, i.parent === r ? (r = o.cache, Yi(t, ca, r), r !== i.cache && Qi(t, [ca], n, !0)) : (i = {
				parent: r,
				cache: r
			}, t.memoizedState = i, t.lanes === 0 && (t.memoizedState = t.updateQueue.baseState = i), Yi(t, ca, r))), ic(e, t, t.pendingProps.children, n), t.child;
			case 29: throw t.pendingProps;
		}
		throw Error(a(156, t.tag));
	}
	function Nc(e) {
		e.flags |= 4;
	}
	function Pc(e, t, n, r, i) {
		if ((t = !!(e.mode & 32)) && (t = !1), t) {
			if (e.flags |= 16777216, (i & 335544128) === i) {
				if (e.stateNode.complete) e.flags |= 8192;
				else if (wu()) e.flags |= 8192;
				else throw Da = Ca, xa;
			}
		} else e.flags &= -16777217;
	}
	function Fc(e, t) {
		if (t.type !== "stylesheet" || t.state.loading & 4) e.flags &= -16777217;
		else if (e.flags |= 16777216, !Wf(t)) {
			if (wu()) e.flags |= 8192;
			else throw Da = Ca, xa;
		}
	}
	function Ic(e, t) {
		t !== null && (e.flags |= 4), e.flags & 16384 && (t = e.tag === 22 ? 536870912 : $e(), e.lanes |= t, Yl |= t);
	}
	function Lc(e, t) {
		if (!R) switch (e.tailMode) {
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
	function W(e) {
		var t = e.alternate !== null && e.alternate.child === e.child, n = 0, r = 0;
		if (t) for (var i = e.child; i !== null;) n |= i.lanes | i.childLanes, r |= i.subtreeFlags & 65011712, r |= i.flags & 65011712, i.return = e, i = i.sibling;
		else for (i = e.child; i !== null;) n |= i.lanes | i.childLanes, r |= i.subtreeFlags, r |= i.flags, i.return = e, i = i.sibling;
		return e.subtreeFlags |= r, e.childLanes = n, t;
	}
	function Rc(e, t, n) {
		var r = t.pendingProps;
		switch (Ni(t), t.tag) {
			case 16:
			case 15:
			case 0:
			case 11:
			case 7:
			case 8:
			case 12:
			case 9:
			case 14: return W(t), null;
			case 1: return W(t), null;
			case 3: return n = t.stateNode, r = null, e !== null && (r = e.memoizedState.cache), t.memoizedState.cache !== r && (t.flags |= 2048), Xi(ca), he(), n.pendingContext && (n.context = n.pendingContext, n.pendingContext = null), (e === null || e.child === null) && (Hi(t) ? Nc(t) : e === null || e.memoizedState.isDehydrated && !(t.flags & 256) || (t.flags |= 1024, Wi())), W(t), null;
			case 26:
				var i = t.type, o = t.memoizedState;
				return e === null ? (Nc(t), o === null ? (W(t), Pc(t, i, null, r, n)) : (W(t), Fc(t, o))) : o ? o === e.memoizedState ? (W(t), t.flags &= -16777217) : (Nc(t), W(t), Fc(t, o)) : (e = e.memoizedProps, e !== r && Nc(t), W(t), Pc(t, i, e, r, n)), null;
			case 27:
				if (_e(t), n = fe.current, i = t.type, e !== null && t.stateNode != null) e.memoizedProps !== r && Nc(t);
				else {
					if (!r) {
						if (t.stateNode === null) throw Error(a(166));
						return W(t), null;
					}
					e = ue.current, Hi(t) ? Bi(t, e) : (e = ff(i, r, n), t.stateNode = e, Nc(t));
				}
				return W(t), null;
			case 5:
				if (_e(t), i = t.type, e !== null && t.stateNode != null) e.memoizedProps !== r && Nc(t);
				else {
					if (!r) {
						if (t.stateNode === null) throw Error(a(166));
						return W(t), null;
					}
					if (o = ue.current, Hi(t)) Bi(t, o);
					else {
						var s = Bd(fe.current);
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
						o[dt] = t, o[ft] = r;
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
						r && Nc(t);
					}
				}
				return W(t), Pc(t, t.type, e === null ? null : e.memoizedProps, t.pendingProps, n), null;
			case 6:
				if (e && t.stateNode != null) e.memoizedProps !== r && Nc(t);
				else {
					if (typeof r != "string" && t.stateNode === null) throw Error(a(166));
					if (e = fe.current, Hi(t)) {
						if (e = t.stateNode, n = t.memoizedProps, r = null, i = Fi, i !== null) switch (i.tag) {
							case 27:
							case 5: r = i.memoizedProps;
						}
						e[dt] = t, e = !!(e.nodeValue === n || r !== null && !0 === r.suppressHydrationWarning || Md(e.nodeValue, n)), e || zi(t, !0);
					} else e = Bd(e).createTextNode(r), e[dt] = t, t.stateNode = e;
				}
				return W(t), null;
			case 31:
				if (n = t.memoizedState, e === null || e.memoizedState !== null) {
					if (r = Hi(t), n !== null) {
						if (e === null) {
							if (!r) throw Error(a(318));
							if (e = t.memoizedState, e = e === null ? null : e.dehydrated, !e) throw Error(a(557));
							e[dt] = t;
						} else Ui(), !(t.flags & 128) && (t.memoizedState = null), t.flags |= 4;
						W(t), e = !1;
					} else n = Wi(), e !== null && e.memoizedState !== null && (e.memoizedState.hydrationErrors = n), e = !0;
					if (!e) return t.flags & 256 ? (so(t), t) : (so(t), null);
					if (t.flags & 128) throw Error(a(558));
				}
				return W(t), null;
			case 13:
				if (r = t.memoizedState, e === null || e.memoizedState !== null && e.memoizedState.dehydrated !== null) {
					if (i = Hi(t), r !== null && r.dehydrated !== null) {
						if (e === null) {
							if (!i) throw Error(a(318));
							if (i = t.memoizedState, i = i === null ? null : i.dehydrated, !i) throw Error(a(317));
							i[dt] = t;
						} else Ui(), !(t.flags & 128) && (t.memoizedState = null), t.flags |= 4;
						W(t), i = !1;
					} else i = Wi(), e !== null && e.memoizedState !== null && (e.memoizedState.hydrationErrors = i), i = !0;
					if (!i) return t.flags & 256 ? (so(t), t) : (so(t), null);
				}
				return so(t), t.flags & 128 ? (t.lanes = n, t) : (n = r !== null, e = e !== null && e.memoizedState !== null, n && (r = t.child, i = null, r.alternate !== null && r.alternate.memoizedState !== null && r.alternate.memoizedState.cachePool !== null && (i = r.alternate.memoizedState.cachePool.pool), o = null, r.memoizedState !== null && r.memoizedState.cachePool !== null && (o = r.memoizedState.cachePool.pool), o !== i && (r.flags |= 2048)), n !== e && n && (t.child.flags |= 8192), Ic(t, t.updateQueue), W(t), null);
			case 4: return he(), e === null && Sd(t.stateNode.containerInfo), W(t), null;
			case 10: return Xi(t.type), W(t), null;
			case 19:
				if (le(co), r = t.memoizedState, r === null) return W(t), null;
				if (i = !!(t.flags & 128), o = r.rendering, o === null) {
					if (i) Lc(r, !1);
					else {
						if (Wl !== 0 || e !== null && e.flags & 128) for (e = t.child; e !== null;) {
							if (o = lo(e), o !== null) {
								for (t.flags |= 128, Lc(r, !1), e = o.updateQueue, t.updateQueue = e, Ic(t, e), t.subtreeFlags = 0, e = n, n = t.child; n !== null;) pi(n, e), n = n.sibling;
								return N(co, co.current & 1 | 2), R && Ai(t, r.treeForkCount), t.child;
							}
							e = e.sibling;
						}
						r.tail !== null && Ae() > tu && (t.flags |= 128, i = !0, Lc(r, !1), t.lanes = 4194304);
					}
				} else {
					if (!i) {
						if (e = lo(o), e !== null) {
							if (t.flags |= 128, i = !0, e = e.updateQueue, t.updateQueue = e, Ic(t, e), Lc(r, !0), r.tail === null && r.tailMode === "hidden" && !o.alternate && !R) return W(t), null;
						} else 2 * Ae() - r.renderingStartTime > tu && n !== 536870912 && (t.flags |= 128, i = !0, Lc(r, !1), t.lanes = 4194304);
					}
					r.isBackwards ? (o.sibling = t.child, t.child = o) : (e = r.last, e === null ? t.child = o : e.sibling = o, r.last = o);
				}
				return r.tail === null ? (W(t), null) : (e = r.tail, r.rendering = e, r.tail = e.sibling, r.renderingStartTime = Ae(), e.sibling = null, n = co.current, N(co, i ? n & 1 | 2 : n & 1), R && Ai(t, r.treeForkCount), e);
			case 22:
			case 23: return so(t), eo(), r = t.memoizedState !== null, e === null ? r && (t.flags |= 8192) : e.memoizedState !== null !== r && (t.flags |= 8192), r ? n & 536870912 && !(t.flags & 128) && (W(t), t.subtreeFlags & 6 && (t.flags |= 8192)) : W(t), n = t.updateQueue, n !== null && Ic(t, n.retryQueue), n = null, e !== null && e.memoizedState !== null && e.memoizedState.cachePool !== null && (n = e.memoizedState.cachePool.pool), r = null, t.memoizedState !== null && t.memoizedState.cachePool !== null && (r = t.memoizedState.cachePool.pool), r !== n && (t.flags |= 2048), e !== null && le(z), null;
			case 24: return n = null, e !== null && (n = e.memoizedState.cache), t.memoizedState.cache !== n && (t.flags |= 2048), Xi(ca), W(t), null;
			case 25: return null;
			case 30: return null;
		}
		throw Error(a(156, t.tag));
	}
	function zc(e, t) {
		switch (Ni(t), t.tag) {
			case 1: return e = t.flags, e & 65536 ? (t.flags = e & -65537 | 128, t) : null;
			case 3: return Xi(ca), he(), e = t.flags, e & 65536 && !(e & 128) ? (t.flags = e & -65537 | 128, t) : null;
			case 26:
			case 27:
			case 5: return _e(t), null;
			case 31:
				if (t.memoizedState !== null) {
					if (so(t), t.alternate === null) throw Error(a(340));
					Ui();
				}
				return e = t.flags, e & 65536 ? (t.flags = e & -65537 | 128, t) : null;
			case 13:
				if (so(t), e = t.memoizedState, e !== null && e.dehydrated !== null) {
					if (t.alternate === null) throw Error(a(340));
					Ui();
				}
				return e = t.flags, e & 65536 ? (t.flags = e & -65537 | 128, t) : null;
			case 19: return le(co), null;
			case 4: return he(), null;
			case 10: return Xi(t.type), null;
			case 22:
			case 23: return so(t), eo(), e !== null && le(z), e = t.flags, e & 65536 ? (t.flags = e & -65537 | 128, t) : null;
			case 24: return Xi(ca), null;
			case 25: return null;
			default: return null;
		}
	}
	function Bc(e, t) {
		switch (Ni(t), t.tag) {
			case 3:
				Xi(ca), he();
				break;
			case 26:
			case 27:
			case 5:
				_e(t);
				break;
			case 4:
				he();
				break;
			case 31:
				t.memoizedState !== null && so(t);
				break;
			case 13:
				so(t);
				break;
			case 19:
				le(co);
				break;
			case 10:
				Xi(t.type);
				break;
			case 22:
			case 23:
				so(t), eo(), e !== null && le(z);
				break;
			case 24: Xi(ca);
		}
	}
	function Vc(e, t) {
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
	function Hc(e, t, n) {
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
	function Uc(e) {
		var t = e.updateQueue;
		if (t !== null) {
			var n = e.stateNode;
			try {
				Ya(t, n);
			} catch (t) {
				Z(e, e.return, t);
			}
		}
	}
	function Wc(e, t, n) {
		n.props = Ks(e.type, e.memoizedProps), n.state = e.memoizedState;
		try {
			n.componentWillUnmount();
		} catch (n) {
			Z(e, t, n);
		}
	}
	function Gc(e, t) {
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
	function Kc(e, t) {
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
	function qc(e) {
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
	function Jc(e, t, n) {
		try {
			var r = e.stateNode;
			Fd(r, e.type, n, t), r[ft] = t;
		} catch (t) {
			Z(e, e.return, t);
		}
	}
	function Yc(e) {
		return e.tag === 5 || e.tag === 3 || e.tag === 26 || e.tag === 27 && Zd(e.type) || e.tag === 4;
	}
	function Xc(e) {
		a: for (;;) {
			for (; e.sibling === null;) {
				if (e.return === null || Yc(e.return)) return null;
				e = e.return;
			}
			for (e.sibling.return = e.return, e = e.sibling; e.tag !== 5 && e.tag !== 6 && e.tag !== 18;) {
				if (e.tag === 27 && Zd(e.type) || e.flags & 2 || e.child === null || e.tag === 4) continue a;
				e.child.return = e, e = e.child;
			}
			if (!(e.flags & 2)) return e.stateNode;
		}
	}
	function Zc(e, t, n) {
		var r = e.tag;
		if (r === 5 || r === 6) e = e.stateNode, t ? (n.nodeType === 9 ? n.body : n.nodeName === "HTML" ? n.ownerDocument.body : n).insertBefore(e, t) : (t = n.nodeType === 9 ? n.body : n.nodeName === "HTML" ? n.ownerDocument.body : n, t.appendChild(e), n = n._reactRootContainer, n != null || t.onclick !== null || (t.onclick = nn));
		else if (r !== 4 && (r === 27 && Zd(e.type) && (n = e.stateNode, t = null), e = e.child, e !== null)) for (Zc(e, t, n), e = e.sibling; e !== null;) Zc(e, t, n), e = e.sibling;
	}
	function Qc(e, t, n) {
		var r = e.tag;
		if (r === 5 || r === 6) e = e.stateNode, t ? n.insertBefore(e, t) : n.appendChild(e);
		else if (r !== 4 && (r === 27 && Zd(e.type) && (n = e.stateNode), e = e.child, e !== null)) for (Qc(e, t, n), e = e.sibling; e !== null;) Qc(e, t, n), e = e.sibling;
	}
	function $c(e) {
		var t = e.stateNode, n = e.memoizedProps;
		try {
			for (var r = e.type, i = t.attributes; i.length;) t.removeAttributeNode(i[0]);
			Pd(t, r, n), t[dt] = e, t[ft] = n;
		} catch (t) {
			Z(e, e.return, t);
		}
	}
	var el = !1, tl = !1, nl = !1, rl = typeof WeakSet == "function" ? WeakSet : Set, il = null;
	function al(e, t) {
		if (e = e.containerInfo, Rd = sp, e = kr(e), Ar(e)) {
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
		}, sp = !1, il = t; il !== null;) if (t = il, e = t.child, t.subtreeFlags & 1028 && e !== null) e.return = t, il = e;
		else for (; il !== null;) {
			switch (t = il, o = t.alternate, e = t.flags, t.tag) {
				case 0:
					if (e & 4 && (e = t.updateQueue, e = e === null ? null : e.events, e !== null)) for (n = 0; n < e.length; n++) i = e[n], i.ref.impl = i.nextImpl;
					break;
				case 11:
				case 15: break;
				case 1:
					if (e & 1024 && o !== null) {
						e = void 0, n = t, i = o.memoizedProps, o = o.memoizedState, r = n.stateNode;
						try {
							var h = Ks(n.type, i);
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
				e.return = t.return, il = e;
				break;
			}
			il = t.return;
		}
	}
	function ol(e, t, n) {
		var r = n.flags;
		switch (n.tag) {
			case 0:
			case 11:
			case 15:
				bl(e, n), r & 4 && Vc(5, n);
				break;
			case 1:
				if (bl(e, n), r & 4) {
					if (e = n.stateNode, t === null) try {
						e.componentDidMount();
					} catch (e) {
						Z(n, n.return, e);
					}
					else {
						var i = Ks(n.type, t.memoizedProps);
						t = t.memoizedState;
						try {
							e.componentDidUpdate(i, t, e.__reactInternalSnapshotBeforeUpdate);
						} catch (e) {
							Z(n, n.return, e);
						}
					}
				}
				r & 64 && Uc(n), r & 512 && Gc(n, n.return);
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
						Ya(e, t);
					} catch (e) {
						Z(n, n.return, e);
					}
				}
				break;
			case 27: t === null && r & 4 && $c(n);
			case 26:
			case 5:
				bl(e, n), t === null && r & 4 && qc(n), r & 512 && Gc(n, n.return);
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
				if (r = n.memoizedState !== null || el, !r) {
					t = t !== null && t.memoizedState !== null || tl, i = el;
					var a = tl;
					el = r, (tl = t) && !a ? Sl(e, n, !!(n.subtreeFlags & 8772)) : bl(e, n), el = i, tl = a;
				}
				break;
			case 30: break;
			default: bl(e, n);
		}
	}
	function sl(e) {
		var t = e.alternate;
		t !== null && (e.alternate = null, sl(t)), e.child = null, e.deletions = null, e.sibling = null, e.tag === 5 && (t = e.stateNode, t !== null && yt(t)), e.stateNode = null, e.return = null, e.dependencies = null, e.memoizedProps = null, e.memoizedState = null, e.pendingProps = null, e.stateNode = null, e.updateQueue = null;
	}
	var G = null, cl = !1;
	function ll(e, t, n) {
		for (n = n.child; n !== null;) ul(e, t, n), n = n.sibling;
	}
	function ul(e, t, n) {
		if (Be && typeof Be.onCommitFiberUnmount == "function") try {
			Be.onCommitFiberUnmount(ze, n);
		} catch {}
		switch (n.tag) {
			case 26:
				tl || Kc(n, t), ll(e, t, n), n.memoizedState ? n.memoizedState.count-- : n.stateNode && (n = n.stateNode, n.parentNode.removeChild(n));
				break;
			case 27:
				tl || Kc(n, t);
				var r = G, i = cl;
				Zd(n.type) && (G = n.stateNode, cl = !1), ll(e, t, n), pf(n.stateNode), G = r, cl = i;
				break;
			case 5: tl || Kc(n, t);
			case 6:
				if (r = G, i = cl, G = null, ll(e, t, n), G = r, cl = i, G !== null) {
					if (cl) try {
						(G.nodeType === 9 ? G.body : G.nodeName === "HTML" ? G.ownerDocument.body : G).removeChild(n.stateNode);
					} catch (e) {
						Z(n, t, e);
					}
					else try {
						G.removeChild(n.stateNode);
					} catch (e) {
						Z(n, t, e);
					}
				}
				break;
			case 18:
				G !== null && (cl ? (e = G, Qd(e.nodeType === 9 ? e.body : e.nodeName === "HTML" ? e.ownerDocument.body : e, n.stateNode), Np(e)) : Qd(G, n.stateNode));
				break;
			case 4:
				r = G, i = cl, G = n.stateNode.containerInfo, cl = !0, ll(e, t, n), G = r, cl = i;
				break;
			case 0:
			case 11:
			case 14:
			case 15:
				Hc(2, n, t), tl || Hc(4, n, t), ll(e, t, n);
				break;
			case 1:
				tl || (Kc(n, t), r = n.stateNode, typeof r.componentWillUnmount == "function" && Wc(n, t, r)), ll(e, t, n);
				break;
			case 21:
				ll(e, t, n);
				break;
			case 22:
				tl = (r = tl) || n.memoizedState !== null, ll(e, t, n), tl = r;
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
				return t === null && (t = e.stateNode = new rl()), t;
			case 22: return e = e.stateNode, t = e._retryCache, t === null && (t = e._retryCache = new rl()), t;
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
							G = c.stateNode, cl = !1;
							break a;
						}
						break;
					case 5:
						G = c.stateNode, cl = !1;
						break a;
					case 3:
					case 4:
						G = c.stateNode.containerInfo, cl = !0;
						break a;
				}
				c = c.return;
			}
			if (G === null) throw Error(a(160));
			ul(o, s, i), G = null, cl = !1, o = i.alternate, o !== null && (o.return = null), i.return = null;
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
				hl(t, e), vl(e), r & 4 && (Hc(3, e, e.return), Vc(3, e), Hc(5, e, e.return));
				break;
			case 1:
				hl(t, e), vl(e), r & 512 && (tl || n === null || Kc(n, n.return)), r & 64 && el && (e = e.updateQueue, e !== null && (r = e.callbacks, r !== null && (n = e.shared.hiddenCallbacks, e.shared.hiddenCallbacks = n === null ? r : n.concat(r))));
				break;
			case 26:
				var i = gl;
				if (hl(t, e), vl(e), r & 512 && (tl || n === null || Kc(n, n.return)), r & 4) {
					var o = n === null ? null : n.memoizedState;
					if (r = e.memoizedState, n === null) {
						if (r === null) {
							if (e.stateNode === null) {
								a: {
									r = e.type, n = e.memoizedProps, i = i.ownerDocument || i;
									b: switch (r) {
										case "title":
											o = i.getElementsByTagName("title")[0], (!o || o[vt] || o[dt] || o.namespaceURI === "http://www.w3.org/2000/svg" || o.hasAttribute("itemprop")) && (o = i.createElement(r), i.head.insertBefore(o, i.querySelector("head > title"))), Pd(o, r, n), o[dt] = e, P(o), r = o;
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
									o[dt] = e, P(o), r = o;
								}
								e.stateNode = r;
							} else Hf(i, e.type, e.stateNode);
						} else e.stateNode = If(i, r, e.memoizedProps);
					} else o === r ? r === null && e.stateNode !== null && Jc(e, e.memoizedProps, n.memoizedProps) : (o === null ? n.stateNode !== null && (n = n.stateNode, n.parentNode.removeChild(n)) : o.count--, r === null ? Hf(i, e.type, e.stateNode) : If(i, r, e.memoizedProps));
				}
				break;
			case 27:
				hl(t, e), vl(e), r & 512 && (tl || n === null || Kc(n, n.return)), n !== null && r & 4 && Jc(e, e.memoizedProps, n.memoizedProps);
				break;
			case 5:
				if (hl(t, e), vl(e), r & 512 && (tl || n === null || Kc(n, n.return)), e.flags & 32) {
					i = e.stateNode;
					try {
						Jt(i, "");
					} catch (t) {
						Z(e, e.return, t);
					}
				}
				r & 4 && e.stateNode != null && (i = e.memoizedProps, Jc(e, i, n === null ? i : n.memoizedProps)), r & 1024 && (nl = !0);
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
				nl && (nl = !1, yl(e));
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
				hl(t, e), vl(e), e.child.flags & 8192 && e.memoizedState !== null != (n !== null && n.memoizedState !== null) && ($l = Ae()), r & 4 && (r = e.updateQueue, r !== null && (e.updateQueue = null, ml(e, r)));
				break;
			case 22:
				i = e.memoizedState !== null;
				var l = n !== null && n.memoizedState !== null, u = el, d = tl;
				if (el = u || i, tl = d || l, hl(t, e), tl = d, el = u, vl(e), r & 8192) a: for (t = e.stateNode, t._visibility = i ? t._visibility & -2 : t._visibility | 1, i && (n === null || l || el || tl || xl(e)), n = null, t = e;;) {
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
					if (Yc(r)) {
						n = r;
						break;
					}
					r = r.return;
				}
				if (n == null) throw Error(a(160));
				switch (n.tag) {
					case 27:
						var i = n.stateNode;
						Qc(e, Xc(e), i);
						break;
					case 5:
						var o = n.stateNode;
						n.flags & 32 && (Jt(o, ""), n.flags &= -33), Qc(e, Xc(e), o);
						break;
					case 3:
					case 4:
						var s = n.stateNode.containerInfo;
						Zc(e, Xc(e), s);
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
		if (t.subtreeFlags & 8772) for (t = t.child; t !== null;) ol(e, t.alternate, t), t = t.sibling;
	}
	function xl(e) {
		for (e = e.child; e !== null;) {
			var t = e;
			switch (t.tag) {
				case 0:
				case 11:
				case 14:
				case 15:
					Hc(4, t, t.return), xl(t);
					break;
				case 1:
					Kc(t, t.return);
					var n = t.stateNode;
					typeof n.componentWillUnmount == "function" && Wc(t, t.return, n), xl(t);
					break;
				case 27: pf(t.stateNode);
				case 26:
				case 5:
					Kc(t, t.return), xl(t);
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
					Sl(i, a, n), Vc(4, a);
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
							if (c !== null) for (i.shared.hiddenCallbacks = null, i = 0; i < c.length; i++) Ja(c[i], s);
						} catch (e) {
							Z(r, r.return, e);
						}
					}
					n && o & 64 && Uc(a), Gc(a, a.return);
					break;
				case 27: $c(a);
				case 26:
				case 5:
					Sl(i, a, n), n && r === null && o & 4 && qc(a), Gc(a, a.return);
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
					a.memoizedState === null && Sl(i, a, n), Gc(a, a.return);
					break;
				case 30: break;
				default: Sl(i, a, n);
			}
			t = t.sibling;
		}
	}
	function Cl(e, t) {
		var n = null;
		e !== null && e.memoizedState !== null && e.memoizedState.cachePool !== null && (n = e.memoizedState.cachePool.pool), e = null, t.memoizedState !== null && t.memoizedState.cachePool !== null && (e = t.memoizedState.cachePool.pool), e !== n && (e != null && e.refCount++, n != null && ua(n));
	}
	function wl(e, t) {
		e = null, t.alternate !== null && (e = t.alternate.memoizedState.cache), t = t.memoizedState.cache, t !== e && (t.refCount++, e != null && ua(e));
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
				Tl(e, t, n, r), i & 2048 && Vc(9, t);
				break;
			case 1:
				Tl(e, t, n, r);
				break;
			case 3:
				Tl(e, t, n, r), i & 2048 && (e = null, t.alternate !== null && (e = t.alternate.memoizedState.cache), t = t.memoizedState.cache, t !== e && (t.refCount++, e != null && ua(e)));
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
					Dl(a, o, s, c, i), Vc(8, o);
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
				il = r, Il(r, e);
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
				Nl(e), e.flags & 2048 && Hc(9, e, e.return);
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
				il = r, Il(r, e);
			}
			Ml(e);
		}
		for (e = e.child; e !== null;) {
			switch (t = e, t.tag) {
				case 0:
				case 11:
				case 15:
					Hc(8, t, t.return), Fl(t);
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
		for (; il !== null;) {
			var n = il;
			switch (n.tag) {
				case 0:
				case 11:
				case 15:
					Hc(8, n, t);
					break;
				case 23:
				case 22:
					if (n.memoizedState !== null && n.memoizedState.cachePool !== null) {
						var r = n.memoizedState.cachePool.pool;
						r != null && r.refCount++;
					}
					break;
				case 24: ua(n.memoizedState.cache);
			}
			if (r = n.child, r !== null) r.return = n, il = r;
			else a: for (n = e; il !== null;) {
				r = il;
				var i = r.sibling, a = r.return;
				if (sl(r), r === n) {
					il = null;
					break a;
				}
				if (i !== null) {
					i.return = a, il = i;
					break a;
				}
				il = a;
			}
		}
	}
	var Ll = {
		getCacheForType: function(e) {
			var t = na(ca), n = t.data.get(e);
			return n === void 0 && (n = e(), t.data.set(e, n)), n;
		},
		cacheSignal: function() {
			return na(ca).controller.signal;
		}
	}, Rl = typeof WeakMap == "function" ? WeakMap : Map, K = 0, q = null, J = null, Y = 0, X = 0, zl = null, Bl = !1, Vl = !1, Hl = !1, Ul = 0, Wl = 0, Gl = 0, Kl = 0, ql = 0, Jl = 0, Yl = 0, Xl = null, Zl = null, Ql = !1, $l = 0, eu = 0, tu = Infinity, nu = null, ru = null, iu = 0, au = null, ou = null, su = 0, cu = 0, lu = null, uu = null, du = 0, fu = null;
	function pu() {
		return K & 2 && Y !== 0 ? Y & -Y : j.T === null ? ct() : dd();
	}
	function mu() {
		if (Jl === 0) {
			if (!(Y & 536870912) || R) {
				var e = qe;
				qe <<= 1, !(qe & 3932160) && (qe = 262144), Jl = e;
			} else Jl = 536870912;
		}
		return e = to.current, e !== null && (e.flags |= 32), Jl;
	}
	function hu(e, t, n) {
		(e === q && (X === 2 || X === 9) || e.cancelPendingCommit !== null) && (Su(e, 0), yu(e, Y, Jl, !1)), tt(e, n), (!(K & 2) || e !== q) && (e === q && (!(K & 2) && (Kl |= n), Wl === 4 && yu(e, Y, Jl, !1)), rd(e));
	}
	function gu(e, t, n) {
		if (K & 6) throw Error(a(327));
		var r = !n && !(t & 127) && (t & e.expiredLanes) === 0 || Ze(e, t), i = r ? Au(e, t) : Ou(e, t, !0), o = r;
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
				if ((t & 62914560) === t && (i = $l + 300 - Ae(), 10 < i)) {
					if (yu(r, t, Jl, !Bl), Xe(r, 0, !0) !== 0) break a;
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
				unsuspend: nn
			}, jl(t, a, d);
			var m = (a & 62914560) === a ? $l - Ae() : (a & 4194048) === a ? eu - Ae() : 0;
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
					if (!wr(a(), i)) return !1;
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
			var a = 31 - He(i), o = 1 << a;
			r[a] = -1, i &= ~o;
		}
		n !== 0 && rt(e, n, t);
	}
	function bu() {
		return K & 6 ? !0 : (id(0, !1), !1);
	}
	function xu() {
		if (J !== null) {
			if (X === 0) var e = J.return;
			else e = J, Ji = qi = null, Oo(e), Aa = null, ja = 0, e = J;
			for (; e !== null;) Bc(e.alternate, e), e = e.return;
			J = null;
		}
	}
	function Su(e, t) {
		var n = e.timeoutHandle;
		n !== -1 && (e.timeoutHandle = -1, qd(n)), n = e.cancelPendingCommit, n !== null && (e.cancelPendingCommit = null, n()), su = 0, xu(), q = e, J = n = fi(e.current, null), Y = t, X = 0, zl = null, Bl = !1, Vl = Ze(e, t), Hl = !1, Yl = Jl = ql = Kl = Gl = Wl = 0, Zl = Xl = null, Ql = !1, t & 8 && (t |= t & 32);
		var r = e.entangledLanes;
		if (r !== 0) for (e = e.entanglements, r &= t; 0 < r;) {
			var i = 31 - He(r), a = 1 << i;
			t |= e[i], r &= ~a;
		}
		return Ul = t, ni(), n;
	}
	function Cu(e, t) {
		H = null, j.H = Rs, t === V || t === Sa ? (t = Oa(), X = 3) : t === xa ? (t = Oa(), X = 4) : X = t === nc ? 8 : typeof t == "object" && t && typeof t.then == "function" ? 6 : 1, zl = t, J === null && (Wl = 1, Xs(e, bi(t, e.current)));
	}
	function wu() {
		var e = to.current;
		return e === null ? !0 : (Y & 4194048) === Y ? no === null : (Y & 62914560) === Y || Y & 536870912 ? e === no : !1;
	}
	function Tu() {
		var e = j.H;
		return j.H = Rs, e === null ? Rs : e;
	}
	function Eu() {
		var e = j.A;
		return j.A = Ll, e;
	}
	function Du() {
		Wl = 4, Bl || (Y & 4194048) !== Y && to.current !== null || (Vl = !0), !(Gl & 134217727) && !(Kl & 134217727) || q === null || yu(q, Y, Jl, !1);
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
							to.current === null && (t = !0);
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
		return t && e.shellSuspendCounter++, Ji = qi = null, K = r, j.H = i, j.A = a, J === null && (q = null, Y = 0, ni()), o;
	}
	function ku() {
		for (; J !== null;) Mu(J);
	}
	function Au(e, t) {
		var n = K;
		K |= 2;
		var r = Tu(), i = Eu();
		q !== e || Y !== t ? (nu = null, tu = Ae() + 500, Su(e, t)) : Vl = Ze(e, t);
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
							if (wa(o)) {
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
							wa(o) ? (X = 0, zl = null, Nu(t)) : (X = 0, zl = null, Pu(e, t, o, 7));
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
		return Ji = qi = null, j.H = r, j.A = i, K = n, J === null ? (q = null, Y = 0, ni(), Wl) : 0;
	}
	function ju() {
		for (; J !== null && !Oe();) Mu(J);
	}
	function Mu(e) {
		var t = Mc(e.alternate, e, Ul);
		e.memoizedProps = e.pendingProps, t === null ? Fu(e) : J = t;
	}
	function Nu(e) {
		var t = e, n = t.alternate;
		switch (t.tag) {
			case 15:
			case 0:
				t = gc(n, t, t.pendingProps, t.type, void 0, Y);
				break;
			case 11:
				t = gc(n, t, t.pendingProps, t.type.render, t.ref, Y);
				break;
			case 5: Oo(t);
			default: Bc(n, t), t = J = pi(t, Ul), t = Mc(n, t, Ul);
		}
		e.memoizedProps = e.pendingProps, t === null ? Fu(e) : J = t;
	}
	function Pu(e, t, n, r) {
		Ji = qi = null, Oo(t), Aa = null, ja = 0;
		var i = t.return;
		try {
			if (tc(e, i, t, n, Y)) {
				Wl = 1, Xs(e, bi(n, e.current)), J = null;
				return;
			}
		} catch (t) {
			if (i !== null) throw J = i, t;
			Wl = 1, Xs(e, bi(n, e.current)), J = null;
			return;
		}
		t.flags & 32768 ? (R || r === 1 ? e = !0 : Vl || Y & 536870912 ? e = !1 : (Bl = e = !0, (r === 2 || r === 9 || r === 3 || r === 6) && (r = to.current, r !== null && r.tag === 13 && (r.flags |= 16384))), Iu(t, e)) : Fu(t);
	}
	function Fu(e) {
		var t = e;
		do {
			if (t.flags & 32768) {
				Iu(t, Bl);
				return;
			}
			e = t.return;
			var n = Rc(t.alternate, t, Ul);
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
			var n = zc(e.alternate, e);
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
			if (o = t.lanes | t.childLanes, o |= ti, nt(e, n, o, s, c, l), e === q && (J = q = null, Y = 0), ou = t, au = e, su = n, cu = o, lu = i, uu = r, t.subtreeFlags & 10256 || t.flags & 10256 ? (e.callbackNode = null, e.callbackPriority = 0, Xu(Pe, function() {
				return Uu(), null;
			})) : (e.callbackNode = null, e.callbackPriority = 0), r = !!(t.flags & 13878), t.subtreeFlags & 13878 || r) {
				r = j.T, j.T = null, i = M.p, M.p = 2, s = K, K |= 4;
				try {
					al(e, t, n);
				} finally {
					K = s, M.p = i, j.T = r;
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
				n = j.T, j.T = null;
				var r = M.p;
				M.p = 2;
				var i = K;
				K |= 4;
				try {
					_l(t, e);
					var a = zd, o = kr(e.containerInfo), s = a.focusedElem, c = a.selectionRange;
					if (o !== s && s && s.ownerDocument && Or(s.ownerDocument.documentElement, s)) {
						if (c !== null && Ar(s)) {
							var l = c.start, u = c.end;
							if (u === void 0 && (u = l), "selectionStart" in s) s.selectionStart = l, s.selectionEnd = Math.min(u, s.value.length);
							else {
								var d = s.ownerDocument || document, f = d && d.defaultView || window;
								if (f.getSelection) {
									var p = f.getSelection(), m = s.textContent.length, h = Math.min(c.start, m), g = c.end === void 0 ? h : Math.min(c.end, m);
									!p.extend && h > g && (o = g, g = h, h = o);
									var _ = Dr(s, h), v = Dr(s, g);
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
					K = i, M.p = r, j.T = n;
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
				n = j.T, j.T = null;
				var r = M.p;
				M.p = 2;
				var i = K;
				K |= 4;
				try {
					ol(e, t.alternate, t);
				} finally {
					K = i, M.p = r, j.T = n;
				}
			}
			iu = 3;
		}
	}
	function Bu() {
		if (iu === 4 || iu === 3) {
			iu = 0, ke();
			var e = au, t = ou, n = su, r = uu;
			t.subtreeFlags & 10256 || t.flags & 10256 ? iu = 5 : (iu = 0, ou = au = null, Vu(e, e.pendingLanes));
			var i = e.pendingLanes;
			if (i === 0 && (ru = null), st(n), t = t.stateNode, Be && typeof Be.onCommitFiberRoot == "function") try {
				Be.onCommitFiberRoot(ze, t, void 0, (t.current.flags & 128) == 128);
			} catch {}
			if (r !== null) {
				t = j.T, i = M.p, M.p = 2, j.T = null;
				try {
					for (var a = e.onRecoverableError, o = 0; o < r.length; o++) {
						var s = r[o];
						a(s.value, { componentStack: s.stack });
					}
				} finally {
					j.T = t, M.p = i;
				}
			}
			su & 3 && Hu(), rd(e), i = e.pendingLanes, n & 261930 && i & 42 ? e === fu ? du++ : (du = 0, fu = e) : du = 0, id(0, !1);
		}
	}
	function Vu(e, t) {
		(e.pooledCacheLanes &= t) === 0 && (t = e.pooledCache, t != null && (e.pooledCache = null, ua(t)));
	}
	function Hu() {
		return Ru(), zu(), Bu(), Uu();
	}
	function Uu() {
		if (iu !== 5) return !1;
		var e = au, t = cu;
		cu = 0;
		var n = st(su), r = j.T, i = M.p;
		try {
			M.p = 32 > n ? 32 : n, j.T = null, n = lu, lu = null;
			var o = au, s = su;
			if (iu = 0, ou = au = null, su = 0, K & 6) throw Error(a(331));
			var c = K;
			if (K |= 4, Pl(o.current), El(o, o.current, s, n), K = c, id(0, !1), Be && typeof Be.onPostCommitFiberRoot == "function") try {
				Be.onPostCommitFiberRoot(ze, o);
			} catch {}
			return !0;
		} finally {
			M.p = i, j.T = r, Vu(e, t);
		}
	}
	function Wu(e, t, n) {
		t = bi(n, t), t = Qs(e.stateNode, t, 2), e = Ha(e, t, 2), e !== null && (tt(e, 2), rd(e));
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
					e = bi(n, e), n = $s(2), r = Ha(t, n, 2), r !== null && (ec(n, r, t, e), tt(r, 2), rd(r));
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
		r !== null && r.delete(t), e.pingedLanes |= e.suspendedLanes & n, e.warmLanes &= ~n, q === e && (Y & n) === n && (Wl === 4 || Wl === 3 && (Y & 62914560) === Y && 300 > Ae() - $l ? !(K & 2) && Su(e, 0) : ql |= n, Yl === Y && (Yl = 0)), rd(e);
	}
	function qu(e, t) {
		t === 0 && (t = $e()), e = ai(e, t), e !== null && (tt(e, t), rd(e));
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
		return Ee(e, t);
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
								a = (1 << 31 - He(42 | e) + 1) - 1, a &= i & ~(o & ~s), a = a & 201326741 ? a & 201326741 | 1 : a ? a | 2 : 0;
							}
							a !== 0 && (n = !0, ld(r, a));
						} else a = Y, a = Xe(r, r === q ? a : 0, r.cancelPendingCommit !== null || r.timeoutHandle !== -1), !(a & 3) || Ze(r, a) || (n = !0, ld(r, a));
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
		for (var t = Ae(), n = null, r = Zu; r !== null;) {
			var i = r.next, a = sd(r, t);
			a === 0 ? (r.next = null, n === null ? Zu = i : n.next = i, i === null && (Qu = n)) : (n = r, (e !== 0 || a & 3) && (ed = !0)), r = i;
		}
		iu !== 0 && iu !== 5 || id(e, !1), nd !== 0 && (nd = 0);
	}
	function sd(e, t) {
		for (var n = e.suspendedLanes, r = e.pingedLanes, i = e.expirationTimes, a = e.pendingLanes & -62914561; 0 < a;) {
			var o = 31 - He(a), s = 1 << o, c = i[o];
			c === -1 ? ((s & n) === 0 || (s & r) !== 0) && (i[o] = Qe(s, t)) : c <= t && (e.expiredLanes |= s), a &= ~s;
		}
		if (t = q, n = Y, n = Xe(e, e === t ? n : 0, e.cancelPendingCommit !== null || e.timeoutHandle !== -1), r = e.callbackNode, n === 0 || e === t && (X === 2 || X === 9) || e.cancelPendingCommit !== null) return r !== null && r !== null && De(r), e.callbackNode = null, e.callbackPriority = 0;
		if (!(n & 3) || Ze(e, n)) {
			if (t = n & -n, t === e.callbackPriority) return t;
			switch (r !== null && De(r), st(n)) {
				case 2:
				case 8:
					n = Ne;
					break;
				case 32:
					n = Pe;
					break;
				case 268435456:
					n = Ie;
					break;
				default: n = Pe;
			}
			return r = cd.bind(null, e), n = Ee(n, r), e.callbackPriority = t, e.callbackNode = n, t;
		}
		return r !== null && r !== null && De(r), e.callbackPriority = 2, e.callbackNode = null, 2;
	}
	function cd(e, t) {
		if (iu !== 0 && iu !== 5) return e.callbackNode = null, e.callbackPriority = 0, null;
		var n = e.callbackNode;
		if (Hu() && e.callbackNode !== n) return null;
		var r = Y;
		return r = Xe(e, e === q ? r : 0, e.cancelPendingCommit !== null || e.timeoutHandle !== -1), r === 0 ? null : (gu(e, r, t), sd(e, Ae()), e.callbackNode != null && e.callbackNode === n ? cd.bind(null, e) : null);
	}
	function ld(e, t) {
		if (Hu()) return null;
		gu(e, t, !0);
	}
	function ud() {
		Yd(function() {
			K & 6 ? Ee(Me, ad) : od();
		});
	}
	function dd() {
		if (nd === 0) {
			var e = pa;
			e === 0 && (e = Ke, Ke <<= 1, !(Ke & 261888) && (Ke = 256)), nd = e;
		}
		return nd;
	}
	function fd(e) {
		return e == null || typeof e == "symbol" || typeof e == "boolean" ? null : typeof e == "function" ? e : tn("" + e);
	}
	function pd(e, t) {
		var n = t.ownerDocument.createElement("input");
		return n.name = t.name, n.value = t.value, e.id && n.setAttribute("form", e.id), t.parentNode.insertBefore(n, t), e = new FormData(e), n.parentNode.removeChild(n), e;
	}
	function md(e, t, n, r, i) {
		if (t === "submit" && n && n.stateNode === i) {
			var a = fd((i[ft] || null).action), o = r.submitter;
			o && (t = (t = o[ft] || null) ? fd(t.formAction) : o.getAttribute("formAction"), t !== null && (a = t, o = null));
			var s = new Cn("action", "action", null, r, i);
			e.push({
				event: s,
				listeners: [{
					instance: null,
					listener: function() {
						if (r.defaultPrevented) {
							if (nd !== 0) {
								var e = o ? pd(i, o) : new FormData(i);
								ws(n, {
									pending: !0,
									data: e,
									method: i.method,
									action: a
								}, null, e);
							}
						} else typeof a == "function" && (s.preventDefault(), e = o ? pd(i, o) : new FormData(i), ws(n, {
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
	for (var hd = 0; hd < Xr.length; hd++) {
		var gd = Xr[hd];
		Zr(gd.toLowerCase(), "on" + (gd[0].toUpperCase() + gd.slice(1)));
	}
	Zr(Hr, "onAnimationEnd"), Zr(Ur, "onAnimationIteration"), Zr(Wr, "onAnimationStart"), Zr("dblclick", "onDoubleClick"), Zr("focusin", "onFocus"), Zr("focusout", "onBlur"), Zr(Gr, "onTransitionRun"), Zr(Kr, "onTransitionStart"), Zr(qr, "onTransitionCancel"), Zr(Jr, "onTransitionEnd"), Dt("onMouseEnter", ["mouseout", "mouseover"]), Dt("onMouseLeave", ["mouseout", "mouseover"]), Dt("onPointerEnter", ["pointerout", "pointerover"]), Dt("onPointerLeave", ["pointerout", "pointerover"]), Et("onChange", "change click focusin focusout input keydown keyup selectionchange".split(" ")), Et("onSelect", "focusout contextmenu dragend focusin keydown keyup mousedown mouseup selectionchange".split(" ")), Et("onBeforeInput", [
		"compositionend",
		"keypress",
		"textInput",
		"paste"
	]), Et("onCompositionEnd", "compositionend focusout keydown keypress keyup mousedown".split(" ")), Et("onCompositionStart", "compositionstart focusout keydown keypress keyup mousedown".split(" ")), Et("onCompositionUpdate", "compositionupdate focusout keydown keypress keyup mousedown".split(" "));
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
						Qr(e);
					}
					i.currentTarget = null, a = c;
				}
				else for (o = 0; o < r.length; o++) {
					if (s = r[o], c = s.instance, l = s.currentTarget, s = s.listener, c !== a && i.isPropagationStopped()) break a;
					a = s, i.currentTarget = l;
					try {
						a(i);
					} catch (e) {
						Qr(e);
					}
					i.currentTarget = null, a = c;
				}
			}
		}
	}
	function Q(e, t) {
		var n = t[mt];
		n === void 0 && (n = t[mt] = /* @__PURE__ */ new Set());
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
			e[xd] = !0, wt.forEach(function(t) {
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
		n = i.bind(null, t, n, e), i = void 0, !fn || t !== "touchstart" && t !== "touchmove" && t !== "wheel" || (i = !0), r ? i === void 0 ? e.addEventListener(t, n, !0) : e.addEventListener(t, n, {
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
					if (o = bt(s), o === null) return;
					if (c = o.tag, c === 5 || c === 6 || c === 26 || c === 27) {
						r = a = o;
						continue a;
					}
					s = s.parentNode;
				}
			}
			r = r.return;
		}
		ln(function() {
			var r = a, i = an(n), o = [];
			a: {
				var s = Yr.get(e);
				if (s !== void 0) {
					var c = Cn, u = e;
					switch (e) {
						case "keypress": if (vn(n) === 0) break a;
						case "keydown":
						case "keyup":
							c = Vn;
							break;
						case "focusin":
							u = "focus", c = Mn;
							break;
						case "focusout":
							u = "blur", c = Mn;
							break;
						case "beforeblur":
						case "afterblur":
							c = Mn;
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
							c = An;
							break;
						case "drag":
						case "dragend":
						case "dragenter":
						case "dragexit":
						case "dragleave":
						case "dragover":
						case "dragstart":
						case "drop":
							c = jn;
							break;
						case "touchcancel":
						case "touchend":
						case "touchmove":
						case "touchstart":
							c = Un;
							break;
						case Hr:
						case Ur:
						case Wr:
							c = Nn;
							break;
						case Jr:
							c = Wn;
							break;
						case "scroll":
						case "scrollend":
							c = Tn;
							break;
						case "wheel":
							c = Gn;
							break;
						case "copy":
						case "cut":
						case "paste":
							c = Pn;
							break;
						case "gotpointercapture":
						case "lostpointercapture":
						case "pointercancel":
						case "pointerdown":
						case "pointermove":
						case "pointerout":
						case "pointerover":
						case "pointerup":
							c = Hn;
							break;
						case "toggle":
						case "beforetoggle": c = Kn;
					}
					var d = !!(t & 4), f = !d && (e === "scroll" || e === "scrollend"), p = d ? s === null ? null : s + "Capture" : s;
					d = [];
					for (var m = r, h; m !== null;) {
						var g = m;
						if (h = g.stateNode, g = g.tag, g !== 5 && g !== 26 && g !== 27 || h === null || p === null || (g = un(m, p), g != null && d.push(Td(m, g, h))), f) break;
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
					if (s = e === "mouseover" || e === "pointerover", c = e === "mouseout" || e === "pointerout", s && n !== rn && (u = n.relatedTarget || n.fromElement) && (bt(u) || u[pt])) break a;
					if ((c || s) && (s = i.window === i ? i : (s = i.ownerDocument) ? s.defaultView || s.parentWindow : window, c ? (u = n.relatedTarget || n.toElement, c = r, u = u ? bt(u) : null, u !== null && (f = l(u), d = u.tag, u !== f || d !== 5 && d !== 27 && d !== 6) && (u = null)) : (c = null, u = r), c !== u)) {
						if (d = An, g = "onMouseLeave", p = "onMouseEnter", m = "mouse", (e === "pointerout" || e === "pointerover") && (d = Hn, g = "onPointerLeave", p = "onPointerEnter", m = "pointer"), f = c == null ? s : St(c), h = u == null ? s : St(u), s = new d(g, m + "leave", c, n, i), s.target = f, s.relatedTarget = h, g = null, bt(i) === r && (d = new d(p, m + "enter", u, n, i), d.target = h, d.relatedTarget = f, g = d), f = g, c && u) b: {
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
					if (s = r ? St(r) : window, c = s.nodeName && s.nodeName.toLowerCase(), c === "select" || c === "input" && s.type === "file") var v = fr;
					else if (or(s)) {
						if (pr) v = Sr;
						else {
							v = br;
							var y = yr;
						}
					} else c = s.nodeName, !c || c.toLowerCase() !== "input" || s.type !== "checkbox" && s.type !== "radio" ? r && Qt(r.elementType) && (v = fr) : v = xr;
					if (v &&= v(e, r)) {
						sr(o, v, n, i);
						break a;
					}
					y && y(e, s, r), e === "focusout" && r && s.type === "number" && r.memoizedProps.value != null && Wt(s, "number", s.value);
				}
				switch (y = r ? St(r) : window, e) {
					case "focusin":
						(or(y) || y.contentEditable === "true") && (Mr = y, Nr = r, Pr = null);
						break;
					case "focusout":
						Pr = Nr = Mr = null;
						break;
					case "mousedown":
						Fr = !0;
						break;
					case "contextmenu":
					case "mouseup":
					case "dragend":
						Fr = !1, Ir(o, n, i);
						break;
					case "selectionchange": if (jr) break;
					case "keydown":
					case "keyup": Ir(o, n, i);
				}
				var b;
				if (Jn) b: {
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
				else nr ? er(e, n) && (x = "onCompositionEnd") : e === "keydown" && n.keyCode === 229 && (x = "onCompositionStart");
				x && (Zn && n.locale !== "ko" && (nr || x !== "onCompositionStart" ? x === "onCompositionEnd" && nr && (b = _n()) : (mn = i, hn = "value" in mn ? mn.value : mn.textContent, nr = !0)), y = Ed(r, x), 0 < y.length && (x = new Fn(x, e, null, n, i), o.push({
					event: x,
					listeners: y
				}), b ? x.data = b : (b = tr(n), b !== null && (x.data = b)))), (b = Xn ? rr(e, n) : ir(e, n)) && (x = Ed(r, "onBeforeInput"), 0 < x.length && (y = new Fn("onBeforeInput", "beforeinput", null, n, i), o.push({
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
			if (i = i.tag, i !== 5 && i !== 26 && i !== 27 || a === null || (i = un(e, n), i != null && r.unshift(Td(e, i, a)), i = un(e, t), i != null && r.push(Td(e, i, a))), e.tag === 3) return r;
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
			s !== 5 && s !== 26 && s !== 27 || l === null || (c = l, i ? (l = un(n, a), l != null && o.unshift(Td(n, l, c))) : i || (l = un(n, a), l != null && o.push(Td(n, l, c)))), n = n.return;
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
				typeof r == "string" ? t === "body" || t === "textarea" && r === "" || Jt(e, r) : (typeof r == "number" || typeof r == "bigint") && t !== "body" && Jt(e, "" + r);
				break;
			case "className":
				Nt(e, "class", r);
				break;
			case "tabIndex":
				Nt(e, "tabindex", r);
				break;
			case "dir":
			case "role":
			case "viewBox":
			case "width":
			case "height":
				Nt(e, n, r);
				break;
			case "style":
				Zt(e, r, o);
				break;
			case "data": if (t !== "object") {
				Nt(e, "data", r);
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
				r = tn("" + r), e.setAttribute(n, r);
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
				r = tn("" + r), e.setAttribute(n, r);
				break;
			case "onClick":
				r != null && (e.onclick = nn);
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
				n = tn("" + r), e.setAttributeNS("http://www.w3.org/1999/xlink", "xlink:href", n);
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
				Q("beforetoggle", e), Q("toggle", e), Mt(e, "popover", r);
				break;
			case "xlinkActuate":
				Pt(e, "http://www.w3.org/1999/xlink", "xlink:actuate", r);
				break;
			case "xlinkArcrole":
				Pt(e, "http://www.w3.org/1999/xlink", "xlink:arcrole", r);
				break;
			case "xlinkRole":
				Pt(e, "http://www.w3.org/1999/xlink", "xlink:role", r);
				break;
			case "xlinkShow":
				Pt(e, "http://www.w3.org/1999/xlink", "xlink:show", r);
				break;
			case "xlinkTitle":
				Pt(e, "http://www.w3.org/1999/xlink", "xlink:title", r);
				break;
			case "xlinkType":
				Pt(e, "http://www.w3.org/1999/xlink", "xlink:type", r);
				break;
			case "xmlBase":
				Pt(e, "http://www.w3.org/XML/1998/namespace", "xml:base", r);
				break;
			case "xmlLang":
				Pt(e, "http://www.w3.org/XML/1998/namespace", "xml:lang", r);
				break;
			case "xmlSpace":
				Pt(e, "http://www.w3.org/XML/1998/namespace", "xml:space", r);
				break;
			case "is":
				Mt(e, "is", r);
				break;
			case "innerText":
			case "textContent": break;
			default: (!(2 < n.length) || n[0] !== "o" && n[0] !== "O" || n[1] !== "n" && n[1] !== "N") && (n = $t.get(n) || n, Mt(e, n, r));
		}
	}
	function Nd(e, t, n, r, i, o) {
		switch (n) {
			case "style":
				Zt(e, r, o);
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
				typeof r == "string" ? Jt(e, r) : (typeof r == "number" || typeof r == "bigint") && Jt(e, "" + r);
				break;
			case "onScroll":
				r != null && Q("scroll", e);
				break;
			case "onScrollEnd":
				r != null && Q("scrollend", e);
				break;
			case "onClick":
				r != null && (e.onclick = nn);
				break;
			case "suppressContentEditableWarning":
			case "suppressHydrationWarning":
			case "innerHTML":
			case "ref": break;
			case "innerText":
			case "textContent": break;
			default: if (!Tt.hasOwnProperty(n)) a: {
				if (n[0] === "o" && n[1] === "n" && (i = n.endsWith("Capture"), t = n.slice(2, i ? n.length - 7 : void 0), o = e[ft] || null, o = o == null ? null : o[n], typeof o == "function" && e.removeEventListener(t, o, i), typeof r == "function")) {
					typeof o != "function" && o !== null && (n in e ? e[n] = null : e.hasAttribute(n) && e.removeAttribute(n)), e.addEventListener(t, r, i);
					break a;
				}
				n in e ? e[n] = r : !0 === r ? e.setAttribute(n, "") : Mt(e, n, r);
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
				Ut(e, o, c, l, u, s, i, !1);
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
				t = o, n = s, e.multiple = !!r, t == null ? n != null && Gt(e, !!r, n, !0) : Gt(e, !!r, t, !1);
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
				qt(e, r, i, o);
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
			default: if (Qt(t)) {
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
				Ht(e, s, c, l, u, d, o, i);
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
				t = c, n = s, r = m, p == null ? !!r != !!n && (t == null ? Gt(e, !!n, n ? [] : "", !1) : Gt(e, !!n, t, !0)) : Gt(e, !!n, p, !1);
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
				Kt(e, p, m);
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
			default: if (Qt(t)) {
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
						a[vt] || s === "SCRIPT" || s === "STYLE" || s === "LINK" && a.rel.toLowerCase() === "stylesheet" || n.removeChild(a), a = o;
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
					ef(n), yt(n);
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
			} else if (!e[vt]) switch (t) {
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
		yt(e);
	}
	var mf = /* @__PURE__ */ new Map(), hf = /* @__PURE__ */ new Set();
	function gf(e) {
		return typeof e.getRootNode == "function" ? e.getRootNode() : e.nodeType === 9 ? e : e.ownerDocument;
	}
	var _f = M.d;
	M.d = {
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
		var t = xt(e);
		t !== null && t.tag === 5 && t.type === "form" ? Es(t) : _f.r(e);
	}
	var bf = typeof document > "u" ? null : document;
	function xf(e, t, n) {
		var r = bf;
		if (r && typeof t == "string" && t) {
			var i = F(t);
			i = "link[rel=\"" + e + "\"][href=\"" + i + "\"]", typeof n == "string" && (i += "[crossorigin=\"" + n + "\"]"), hf.has(i) || (hf.add(i), e = {
				rel: e,
				crossOrigin: n,
				href: t
			}, r.querySelector(i) === null && (t = r.createElement("link"), Pd(t, "link", e), P(t), r.head.appendChild(t)));
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
			var i = "link[rel=\"preload\"][as=\"" + F(t) + "\"]";
			t === "image" && n && n.imageSrcSet ? (i += "[imagesrcset=\"" + F(n.imageSrcSet) + "\"]", typeof n.imageSizes == "string" && (i += "[imagesizes=\"" + F(n.imageSizes) + "\"]")) : i += "[href=\"" + F(e) + "\"]";
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
			}, n), mf.set(a, e), r.querySelector(i) !== null || t === "style" && r.querySelector(jf(a)) || t === "script" && r.querySelector(Ff(a)) || (t = r.createElement("link"), Pd(t, "link", e), P(t), r.head.appendChild(t)));
		}
	}
	function Tf(e, t) {
		_f.m(e, t);
		var n = bf;
		if (n && e) {
			var r = t && typeof t.as == "string" ? t.as : "script", i = "link[rel=\"modulepreload\"][as=\"" + F(r) + "\"][href=\"" + F(e) + "\"]", a = i;
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
				r = n.createElement("link"), Pd(r, "link", e), P(r), n.head.appendChild(r);
			}
		}
	}
	function Ef(e, t, n) {
		_f.S(e, t, n);
		var r = bf;
		if (r && e) {
			var i = Ct(r).hoistableStyles, a = Af(e);
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
					P(c), Pd(c, "link", e), c._p = new Promise(function(e, t) {
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
			var r = Ct(n).hoistableScripts, i = Pf(e), a = r.get(i);
			a || (a = n.querySelector(Ff(i)), a || (e = h({
				src: e,
				async: !0
			}, t), (t = mf.get(i)) && zf(e, t), a = n.createElement("script"), P(a), Pd(a, "link", e), n.head.appendChild(a)), a = {
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
			var r = Ct(n).hoistableScripts, i = Pf(e), a = r.get(i);
			a || (a = n.querySelector(Ff(i)), a || (e = h({
				src: e,
				async: !0,
				type: "module"
			}, t), (t = mf.get(i)) && zf(e, t), a = n.createElement("script"), P(a), Pd(a, "link", e), n.head.appendChild(a)), a = {
				type: "script",
				instance: a,
				count: 1,
				state: null
			}, r.set(i, a));
		}
	}
	function kf(e, t, n, r) {
		var i = (i = fe.current) ? gf(i) : null;
		if (!i) throw Error(a(446));
		switch (e) {
			case "meta":
			case "title": return null;
			case "style": return typeof n.precedence == "string" && typeof n.href == "string" ? (t = Af(n.href), n = Ct(i).hoistableStyles, r = n.get(t), r || (r = {
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
					var o = Ct(i).hoistableStyles, s = o.get(e);
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
			case "script": return t = n.async, n = n.src, typeof n == "string" && t && typeof t != "function" && typeof t != "symbol" ? (t = Pf(n), n = Ct(i).hoistableScripts, r = n.get(t), r || (r = {
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
		return "href=\"" + F(e) + "\"";
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
		}), Pd(t, "link", n), P(t), e.head.appendChild(t));
	}
	function Pf(e) {
		return "[src=\"" + F(e) + "\"]";
	}
	function Ff(e) {
		return "script[async]" + e;
	}
	function If(e, t, n) {
		if (t.count++, t.instance === null) switch (t.type) {
			case "style":
				var r = e.querySelector("style[data-href~=\"" + F(n.href) + "\"]");
				if (r) return t.instance = r, P(r), r;
				var i = h({}, n, {
					"data-href": n.href,
					"data-precedence": n.precedence,
					href: null,
					precedence: null
				});
				return r = (e.ownerDocument || e).createElement("style"), P(r), Pd(r, "style", i), Lf(r, n.precedence, e), t.instance = r;
			case "stylesheet":
				i = Af(n.href);
				var o = e.querySelector(jf(i));
				if (o) return t.state.loading |= 4, t.instance = o, P(o), o;
				r = Mf(n), (i = mf.get(i)) && Rf(r, i), o = (e.ownerDocument || e).createElement("link"), P(o);
				var s = o;
				return s._p = new Promise(function(e, t) {
					s.onload = e, s.onerror = t;
				}), Pd(o, "link", r), t.state.loading |= 4, Lf(o, n.precedence, e), t.instance = o;
			case "script": return o = Pf(n.src), (i = e.querySelector(Ff(o))) ? (t.instance = i, P(i), i) : (r = n, (i = mf.get(o)) && (r = h({}, n), zf(r, i)), e = e.ownerDocument || e, i = e.createElement("script"), P(i), Pd(i, "link", r), e.head.appendChild(i), t.instance = i);
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
			if (!(a[vt] || a[dt] || e === "link" && a.getAttribute("rel") === "stylesheet") && a.namespaceURI !== "http://www.w3.org/2000/svg") {
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
					t = a._p, typeof t == "object" && t && typeof t.then == "function" && (e.count++, e = Jf.bind(e), t.then(e, e)), n.state.loading |= 4, n.instance = a, P(a);
					return;
				}
				a = t.ownerDocument || t, r = Mf(r), (i = mf.get(i)) && Rf(r, i), a = a.createElement("link"), P(a);
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
		_currentValue: ae,
		_currentValue2: ae,
		_threadCount: 0
	};
	function $f(e, t, n, r, i, a, o, s, c) {
		this.tag = 1, this.containerInfo = e, this.pingCache = this.current = this.pendingChildren = null, this.timeoutHandle = -1, this.callbackNode = this.next = this.pendingContext = this.context = this.cancelPendingCommit = null, this.callbackPriority = 0, this.expirationTimes = et(-1), this.entangledLanes = this.shellSuspendCounter = this.errorRecoveryDisabledLanes = this.expiredLanes = this.warmLanes = this.pingedLanes = this.suspendedLanes = this.pendingLanes = 0, this.entanglements = et(0), this.hiddenUpdates = et(null), this.identifierPrefix = r, this.onUncaughtError = i, this.onCaughtError = a, this.onRecoverableError = o, this.pooledCache = null, this.pooledCacheLanes = 0, this.formState = c, this.incompleteTransitions = /* @__PURE__ */ new Map();
	}
	function ep(e, t, n, r, i, a, o, s, c, l, u, d) {
		return e = new $f(e, t, n, o, c, l, u, d, s), t = 1, !0 === a && (t |= 24), a = ui(3, null, null, t), e.current = a, a.stateNode = e, t = la(), t.refCount++, e.pooledCache = t, t.refCount++, a.memoizedState = {
			element: r,
			isDehydrated: n,
			cache: t
		}, za(a), e;
	}
	function tp(e) {
		return e ? (e = ci, e) : ci;
	}
	function np(e, t, n, r, i, a) {
		i = tp(i), r.context === null ? r.context = i : r.pendingContext = i, r = Va(t), r.payload = { element: n }, a = a === void 0 ? null : a, a !== null && (r.callback = a), n = Ha(e, r, t), n !== null && (hu(n, e, t), Ua(n, e, t));
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
			var t = ai(e, 67108864);
			t !== null && hu(t, e, 67108864), ip(e, 67108864);
		}
	}
	function op(e) {
		if (e.tag === 13 || e.tag === 31) {
			var t = pu();
			t = ot(t);
			var n = ai(e, t);
			n !== null && hu(n, e, t), ip(e, t);
		}
	}
	var sp = !0;
	function cp(e, t, n, r) {
		var i = j.T;
		j.T = null;
		var a = M.p;
		try {
			M.p = 2, up(e, t, n, r);
		} finally {
			M.p = a, j.T = i;
		}
	}
	function lp(e, t, n, r) {
		var i = j.T;
		j.T = null;
		var a = M.p;
		try {
			M.p = 8, up(e, t, n, r);
		} finally {
			M.p = a, j.T = i;
		}
	}
	function up(e, t, n, r) {
		if (sp) {
			var i = dp(r);
			if (i === null) wd(e, t, r, fp, n), Cp(e, r);
			else if (Tp(i, e, t, n, r)) r.stopPropagation();
			else if (Cp(e, r), t & 4 && -1 < Sp.indexOf(e)) {
				for (; i !== null;) {
					var a = xt(i);
					if (a !== null) switch (a.tag) {
						case 3:
							if (a = a.stateNode, a.current.memoizedState.isDehydrated) {
								var o = Ye(a.pendingLanes);
								if (o !== 0) {
									var s = a;
									for (s.pendingLanes |= 2, s.entangledLanes |= 2; o;) {
										var c = 1 << 31 - He(o);
										s.entanglements[1] |= c, o &= ~c;
									}
									rd(a), !(K & 6) && (tu = Ae() + 500, id(0, !1));
								}
							}
							break;
						case 31:
						case 13: s = ai(a, 2), s !== null && hu(s, a, 2), bu(), ip(a, 2);
					}
					if (a = dp(r), a === null && wd(e, t, r, fp, n), a === i) break;
					i = a;
				}
				i !== null && r.stopPropagation();
			} else wd(e, t, r, null, n);
		}
	}
	function dp(e) {
		return e = an(e), pp(e);
	}
	var fp = null;
	function pp(e) {
		if (fp = null, e = bt(e), e !== null) {
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
			case "message": switch (je()) {
				case Me: return 2;
				case Ne: return 8;
				case Pe:
				case Fe: return 32;
				case Ie: return 268435456;
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
		}, t !== null && (t = xt(t), t !== null && ap(t)), e) : (e.eventSystemFlags |= r, t = e.targetContainers, i !== null && t.indexOf(i) === -1 && t.push(i), e);
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
		var t = bt(e.target);
		if (t !== null) {
			var n = l(t);
			if (n !== null) {
				if (t = n.tag, t === 13) {
					if (t = u(n), t !== null) {
						e.blockedOn = t, lt(e.priority, function() {
							op(n);
						});
						return;
					}
				} else if (t === 31) {
					if (t = d(n), t !== null) {
						e.blockedOn = t, lt(e.priority, function() {
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
				rn = r, n.target.dispatchEvent(r), rn = null;
			} else return t = xt(n), t !== null && ap(t), e.blockedOn = n, !1;
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
				var a = xt(n);
				a !== null && (e.splice(t, 3), t -= 3, ws(a, {
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
			var i = n[r], a = n[r + 1], o = i[ft] || null;
			if (typeof a == "function") o || Mp(n);
			else if (o) {
				var s = null;
				if (a && a.hasAttribute("formAction")) {
					if (i = a, o = a[ft] || null) s = o.formAction;
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
			np(e.current, 2, null, e, null, null), bu(), t[pt] = null;
		}
	};
	function Ip(e) {
		this._internalRoot = e;
	}
	Ip.prototype.unstable_scheduleHydration = function(e) {
		if (e) {
			var t = ct();
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
	M.findDOMNode = function(e) {
		var t = e._reactInternals;
		if (t === void 0) throw typeof e.render == "function" ? Error(a(188)) : (e = Object.keys(e).join(","), Error(a(268, e)));
		return e = p(t), e = e === null ? null : m(e), e = e === null ? null : e.stateNode, e;
	};
	var Rp = {
		bundleType: 0,
		version: "19.2.8",
		rendererPackageName: "react-dom",
		currentDispatcherRef: j,
		reconcilerVersion: "19.2.8"
	};
	if (typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ < "u") {
		var zp = __REACT_DEVTOOLS_GLOBAL_HOOK__;
		if (!zp.isDisabled && zp.supportsFiber) try {
			ze = zp.inject(Rp), Be = zp;
		} catch {}
	}
	e.createRoot = function(e, t) {
		if (!s(e)) throw Error(a(299));
		var n = !1, r = "", i = qs, o = Js, c = Ys;
		return t != null && (!0 === t.unstable_strictMode && (n = !0), t.identifierPrefix !== void 0 && (r = t.identifierPrefix), t.onUncaughtError !== void 0 && (i = t.onUncaughtError), t.onCaughtError !== void 0 && (o = t.onCaughtError), t.onRecoverableError !== void 0 && (c = t.onRecoverableError)), t = ep(e, 1, !1, null, null, n, r, null, i, o, c, Pp), e[pt] = t.current, Sd(e), new Fp(t);
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
	var t = Symbol.for("react.transitional.element");
	function n(e, n, r) {
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
	e.jsx = n, e.jsxs = n;
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
	}(e, t) || T(e, t) || ee();
}
function x(e) {
	return C(e) || w(e) || T(e) || ee();
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
function ee() {
	throw TypeError("Invalid attempt to destructure non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
}
function D(e, t) {
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
var O = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : typeof global < "u" ? global : typeof self < "u" ? self : {};
function te(e, t) {
	return e(t = { exports: {} }, t.exports), t.exports;
}
var k = te((function(e) {
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
})), A = {
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
}, ne = (0, h.createContext)(A), re = ne.Provider, ie = function() {
	return (0, h.useContext)(ne);
}, j = te((function(e, t) {
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
function M(e) {
	return e.type === "insert";
}
function ae(e) {
	return e.type === "delete";
}
function oe(e) {
	return e.type === "normal";
}
function se(e, t) {
	var n = t.nearbySequences === "zip" ? function(e) {
		return b(e.reduce((function(e, t, n) {
			var r = b(e, 3), i = r[0], a = r[1], o = r[2];
			return a ? M(t) && o >= 0 ? (i.splice(o + 1, 0, t), [
				i,
				t,
				o + 2
			]) : (i.push(t), [
				i,
				t,
				ae(t) && ae(a) ? o : n
			]) : (i.push(t), [
				i,
				t,
				ae(t) ? n : -1
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
function ce(e) {
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
	return j.parse(n).map((function(e) {
		return function(e, t) {
			var n = e.hunks.map((function(e) {
				return se(e, t);
			}));
			return _(_({}, e), {}, { hunks: n });
		}(e, t);
	}));
}
function le(e) {
	return e[0];
}
function N(e) {
	return e[e.length - 1];
}
function ue(e) {
	return [`${e}Start`, `${e}Lines`];
}
function de(e) {
	return e === "old" ? function(e) {
		return M(e) ? -1 : oe(e) ? e.oldLineNumber : e.lineNumber;
	} : function(e) {
		return ae(e) ? -1 : oe(e) ? e.newLineNumber : e.lineNumber;
	};
}
function fe(e, t) {
	return function(n, r) {
		var i = n[e], a = i + n[t];
		return r >= i && r < a;
	};
}
function pe(e, t) {
	return function(n, r, i) {
		var a = n[e] + n[t], o = r[e];
		return i >= a && i < o;
	};
}
function me(e) {
	var t = de(e), n = function(e) {
		var t = b(ue(e), 2), n = fe(t[0], t[1]);
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
function he(e) {
	var t = e === "old" ? "new" : "old", n = b(ue(e), 2), r = n[0], i = n[1], a = b(ue(t), 2), o = a[0], s = a[1], c = de(e), l = de(t), u = fe(r, i), d = pe(r, i);
	return function(e, t) {
		var n = le(e);
		if (t < n[r]) {
			var a = n[r] - t;
			return n[o] - a;
		}
		var f = N(e);
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
				if (oe(v)) return l(v);
				var y = ae(v) ? _ + 1 : _ - 1, b = h.changes[y];
				if (!b) return -1;
				var x = M(v) ? "delete" : "insert";
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
var ge = function(e, t, n, r) {
	for (var i = e.length, a = n + (r ? 1 : -1); r ? a-- : ++a < i;) if (t(e[a], a, e)) return a;
	return -1;
}, _e = function() {
	this.__data__ = [], this.size = 0;
}, ve = function(e, t) {
	return e === t || e != e && t != t;
}, ye = function(e, t) {
	for (var n = e.length; n--;) if (ve(e[n][0], t)) return n;
	return -1;
}, be = Array.prototype.splice, xe = function(e) {
	var t = this.__data__, n = ye(t, e);
	return !(n < 0) && (n == t.length - 1 ? t.pop() : be.call(t, n, 1), --this.size, !0);
}, Se = function(e) {
	var t = this.__data__, n = ye(t, e);
	return n < 0 ? void 0 : t[n][1];
}, Ce = function(e) {
	return ye(this.__data__, e) > -1;
}, we = function(e, t) {
	var n = this.__data__, r = ye(n, e);
	return r < 0 ? (++this.size, n.push([e, t])) : n[r][1] = t, this;
};
function Te(e) {
	var t = -1, n = e == null ? 0 : e.length;
	for (this.clear(); ++t < n;) {
		var r = e[t];
		this.set(r[0], r[1]);
	}
}
Te.prototype.clear = _e, Te.prototype.delete = xe, Te.prototype.get = Se, Te.prototype.has = Ce, Te.prototype.set = we;
var Ee = Te, De = function() {
	this.__data__ = new Ee(), this.size = 0;
}, Oe = function(e) {
	var t = this.__data__, n = t.delete(e);
	return this.size = t.size, n;
}, ke = function(e) {
	return this.__data__.get(e);
}, Ae = function(e) {
	return this.__data__.has(e);
}, je = typeof O == "object" && O && O.Object === Object && O, Me = typeof self == "object" && self && self.Object === Object && self, Ne = je || Me || Function("return this")(), Pe = Ne.Symbol, Fe = Object.prototype, Ie = Fe.hasOwnProperty, Le = Fe.toString, Re = Pe ? Pe.toStringTag : void 0, ze = function(e) {
	var t = Ie.call(e, Re), n = e[Re];
	try {
		e[Re] = void 0;
		var r = !0;
	} catch {}
	var i = Le.call(e);
	return r && (t ? e[Re] = n : delete e[Re]), i;
}, Be = Object.prototype.toString, Ve = function(e) {
	return Be.call(e);
}, He = Pe ? Pe.toStringTag : void 0, Ue = function(e) {
	return e == null ? e === void 0 ? "[object Undefined]" : "[object Null]" : He && He in Object(e) ? ze(e) : Ve(e);
}, We = function(e) {
	var t = typeof e;
	return e != null && (t == "object" || t == "function");
}, Ge = function(e) {
	if (!We(e)) return !1;
	var t = Ue(e);
	return t == "[object Function]" || t == "[object GeneratorFunction]" || t == "[object AsyncFunction]" || t == "[object Proxy]";
}, Ke = Ne["__core-js_shared__"], qe = function() {
	var e = /[^.]+$/.exec(Ke && Ke.keys && Ke.keys.IE_PROTO || "");
	return e ? "Symbol(src)_1." + e : "";
}(), Je = function(e) {
	return !!qe && qe in e;
}, Ye = Function.prototype.toString, Xe = function(e) {
	if (e != null) {
		try {
			return Ye.call(e);
		} catch {}
		try {
			return e + "";
		} catch {}
	}
	return "";
}, Ze = /^\[object .+?Constructor\]$/, Qe = Function.prototype, $e = Object.prototype, et = Qe.toString, tt = $e.hasOwnProperty, nt = RegExp("^" + et.call(tt).replace(/[\\^$.*+?()[\]{}|]/g, "\\$&").replace(/hasOwnProperty|(function).*?(?=\\\()| for .+?(?=\\\])/g, "$1.*?") + "$"), rt = function(e) {
	return !(!We(e) || Je(e)) && (Ge(e) ? nt : Ze).test(Xe(e));
}, it = function(e, t) {
	return e?.[t];
}, at = function(e, t) {
	var n = it(e, t);
	return rt(n) ? n : void 0;
}, ot = at(Ne, "Map"), st = at(Object, "create"), ct = function() {
	this.__data__ = st ? st(null) : {}, this.size = 0;
}, lt = function(e) {
	var t = this.has(e) && delete this.__data__[e];
	return this.size -= +!!t, t;
}, ut = Object.prototype.hasOwnProperty, dt = function(e) {
	var t = this.__data__;
	if (st) {
		var n = t[e];
		return n === "__lodash_hash_undefined__" ? void 0 : n;
	}
	return ut.call(t, e) ? t[e] : void 0;
}, ft = Object.prototype.hasOwnProperty, pt = function(e) {
	var t = this.__data__;
	return st ? t[e] !== void 0 : ft.call(t, e);
}, mt = function(e, t) {
	var n = this.__data__;
	return this.size += +!this.has(e), n[e] = st && t === void 0 ? "__lodash_hash_undefined__" : t, this;
};
function ht(e) {
	var t = -1, n = e == null ? 0 : e.length;
	for (this.clear(); ++t < n;) {
		var r = e[t];
		this.set(r[0], r[1]);
	}
}
ht.prototype.clear = ct, ht.prototype.delete = lt, ht.prototype.get = dt, ht.prototype.has = pt, ht.prototype.set = mt;
var gt = ht, _t = function() {
	this.size = 0, this.__data__ = {
		hash: new gt(),
		map: new (ot || Ee)(),
		string: new gt()
	};
}, vt = function(e) {
	var t = typeof e;
	return t == "string" || t == "number" || t == "symbol" || t == "boolean" ? e !== "__proto__" : e === null;
}, yt = function(e, t) {
	var n = e.__data__;
	return vt(t) ? n[typeof t == "string" ? "string" : "hash"] : n.map;
}, bt = function(e) {
	var t = yt(this, e).delete(e);
	return this.size -= +!!t, t;
}, xt = function(e) {
	return yt(this, e).get(e);
}, St = function(e) {
	return yt(this, e).has(e);
}, Ct = function(e, t) {
	var n = yt(this, e), r = n.size;
	return n.set(e, t), this.size += n.size == r ? 0 : 1, this;
};
function P(e) {
	var t = -1, n = e == null ? 0 : e.length;
	for (this.clear(); ++t < n;) {
		var r = e[t];
		this.set(r[0], r[1]);
	}
}
P.prototype.clear = _t, P.prototype.delete = bt, P.prototype.get = xt, P.prototype.has = St, P.prototype.set = Ct;
var wt = P, Tt = function(e, t) {
	var n = this.__data__;
	if (n instanceof Ee) {
		var r = n.__data__;
		if (!ot || r.length < 199) return r.push([e, t]), this.size = ++n.size, this;
		n = this.__data__ = new wt(r);
	}
	return n.set(e, t), this.size = n.size, this;
};
function Et(e) {
	var t = this.__data__ = new Ee(e);
	this.size = t.size;
}
Et.prototype.clear = De, Et.prototype.delete = Oe, Et.prototype.get = ke, Et.prototype.has = Ae, Et.prototype.set = Tt;
var Dt = Et, Ot = function(e) {
	return this.__data__.set(e, "__lodash_hash_undefined__"), this;
}, kt = function(e) {
	return this.__data__.has(e);
};
function At(e) {
	var t = -1, n = e == null ? 0 : e.length;
	for (this.__data__ = new wt(); ++t < n;) this.add(e[t]);
}
At.prototype.add = At.prototype.push = Ot, At.prototype.has = kt;
var jt = At, Mt = function(e, t) {
	for (var n = -1, r = e == null ? 0 : e.length; ++n < r;) if (t(e[n], n, e)) return !0;
	return !1;
}, Nt = function(e, t) {
	return e.has(t);
}, Pt = function(e, t, n, r, i, a) {
	var o = 1 & n, s = e.length, c = t.length;
	if (s != c && !(o && c > s)) return !1;
	var l = a.get(e), u = a.get(t);
	if (l && u) return l == t && u == e;
	var d = -1, f = !0, p = 2 & n ? new jt() : void 0;
	for (a.set(e, t), a.set(t, e); ++d < s;) {
		var m = e[d], h = t[d];
		if (r) var g = o ? r(h, m, d, t, e, a) : r(m, h, d, e, t, a);
		if (g !== void 0) {
			if (g) continue;
			f = !1;
			break;
		}
		if (p) {
			if (!Mt(t, (function(e, t) {
				if (!Nt(p, t) && (m === e || i(m, e, n, r, a))) return p.push(t);
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
}, Ft = Ne.Uint8Array, It = function(e) {
	var t = -1, n = Array(e.size);
	return e.forEach((function(e, r) {
		n[++t] = [r, e];
	})), n;
}, Lt = function(e) {
	var t = -1, n = Array(e.size);
	return e.forEach((function(e) {
		n[++t] = e;
	})), n;
}, Rt = Pe ? Pe.prototype : void 0, zt = Rt ? Rt.valueOf : void 0, Bt = function(e, t, n, r, i, a, o) {
	switch (n) {
		case "[object DataView]":
			if (e.byteLength != t.byteLength || e.byteOffset != t.byteOffset) return !1;
			e = e.buffer, t = t.buffer;
		case "[object ArrayBuffer]": return !(e.byteLength != t.byteLength || !a(new Ft(e), new Ft(t)));
		case "[object Boolean]":
		case "[object Date]":
		case "[object Number]": return ve(+e, +t);
		case "[object Error]": return e.name == t.name && e.message == t.message;
		case "[object RegExp]":
		case "[object String]": return e == t + "";
		case "[object Map]": var s = It;
		case "[object Set]":
			var c = 1 & r;
			if (s ||= Lt, e.size != t.size && !c) return !1;
			var l = o.get(e);
			if (l) return l == t;
			r |= 2, o.set(e, t);
			var u = Pt(s(e), s(t), r, i, a, o);
			return o.delete(e), u;
		case "[object Symbol]": if (zt) return zt.call(e) == zt.call(t);
	}
	return !1;
}, Vt = function(e, t) {
	for (var n = -1, r = t.length, i = e.length; ++n < r;) e[i + n] = t[n];
	return e;
}, F = Array.isArray, Ht = function(e, t, n) {
	var r = t(e);
	return F(e) ? r : Vt(r, n(e));
}, Ut = function(e, t) {
	for (var n = -1, r = e == null ? 0 : e.length, i = 0, a = []; ++n < r;) {
		var o = e[n];
		t(o, n, e) && (a[i++] = o);
	}
	return a;
}, Wt = function() {
	return [];
}, Gt = Object.prototype.propertyIsEnumerable, Kt = Object.getOwnPropertySymbols, qt = Kt ? function(e) {
	return e == null ? [] : (e = Object(e), Ut(Kt(e), (function(t) {
		return Gt.call(e, t);
	})));
} : Wt, Jt = function(e, t) {
	for (var n = -1, r = Array(e); ++n < e;) r[n] = t(n);
	return r;
}, Yt = function(e) {
	return typeof e == "object" && !!e;
}, Xt = function(e) {
	return Yt(e) && Ue(e) == "[object Arguments]";
}, Zt = Object.prototype, Qt = Zt.hasOwnProperty, $t = Zt.propertyIsEnumerable, en = Xt(function() {
	return arguments;
}()) ? Xt : function(e) {
	return Yt(e) && Qt.call(e, "callee") && !$t.call(e, "callee");
}, tn = function() {
	return !1;
}, nn = te((function(e, t) {
	var n = t && !t.nodeType && t, r = n && e && !e.nodeType && e, i = r && r.exports === n ? Ne.Buffer : void 0;
	e.exports = (i ? i.isBuffer : void 0) || tn;
})), rn = /^(?:0|[1-9]\d*)$/, an = function(e, t) {
	var n = typeof e;
	return !!(t ??= 9007199254740991) && (n == "number" || n != "symbol" && rn.test(e)) && e > -1 && e % 1 == 0 && e < t;
}, on = function(e) {
	return typeof e == "number" && e > -1 && e % 1 == 0 && e <= 9007199254740991;
}, I = {};
I["[object Float32Array]"] = I["[object Float64Array]"] = I["[object Int8Array]"] = I["[object Int16Array]"] = I["[object Int32Array]"] = I["[object Uint8Array]"] = I["[object Uint8ClampedArray]"] = I["[object Uint16Array]"] = I["[object Uint32Array]"] = !0, I["[object Arguments]"] = I["[object Array]"] = I["[object ArrayBuffer]"] = I["[object Boolean]"] = I["[object DataView]"] = I["[object Date]"] = I["[object Error]"] = I["[object Function]"] = I["[object Map]"] = I["[object Number]"] = I["[object Object]"] = I["[object RegExp]"] = I["[object Set]"] = I["[object String]"] = I["[object WeakMap]"] = !1;
var sn = function(e) {
	return Yt(e) && on(e.length) && !!I[Ue(e)];
}, cn = function(e) {
	return function(t) {
		return e(t);
	};
}, ln = te((function(e, t) {
	var n = t && !t.nodeType && t, r = n && e && !e.nodeType && e, i = r && r.exports === n && je.process;
	e.exports = function() {
		try {
			return r && r.require && r.require("util").types || i && i.binding && i.binding("util");
		} catch {}
	}();
})), un = ln && ln.isTypedArray, dn = un ? cn(un) : sn, fn = Object.prototype.hasOwnProperty, pn = function(e, t) {
	var n = F(e), r = !n && en(e), i = !n && !r && nn(e), a = !n && !r && !i && dn(e), o = n || r || i || a, s = o ? Jt(e.length, String) : [], c = s.length;
	for (var l in e) !t && !fn.call(e, l) || o && (l == "length" || i && (l == "offset" || l == "parent") || a && (l == "buffer" || l == "byteLength" || l == "byteOffset") || an(l, c)) || s.push(l);
	return s;
}, mn = Object.prototype, hn = function(e) {
	var t = e && e.constructor;
	return e === (typeof t == "function" && t.prototype || mn);
}, gn = function(e, t) {
	return function(n) {
		return e(t(n));
	};
}(Object.keys, Object), _n = Object.prototype.hasOwnProperty, vn = function(e) {
	if (!hn(e)) return gn(e);
	var t = [];
	for (var n in Object(e)) _n.call(e, n) && n != "constructor" && t.push(n);
	return t;
}, yn = function(e) {
	return e != null && on(e.length) && !Ge(e);
}, bn = function(e) {
	return yn(e) ? pn(e) : vn(e);
}, xn = function(e) {
	return Ht(e, bn, qt);
}, Sn = Object.prototype.hasOwnProperty, Cn = function(e, t, n, r, i, a) {
	var o = 1 & n, s = xn(e), c = s.length;
	if (c != xn(t).length && !o) return !1;
	for (var l = c; l--;) {
		var u = s[l];
		if (!(o ? u in t : Sn.call(t, u))) return !1;
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
}, wn = at(Ne, "DataView"), Tn = at(Ne, "Promise"), En = at(Ne, "Set"), Dn = at(Ne, "WeakMap"), On = Xe(wn), kn = Xe(ot), An = Xe(Tn), jn = Xe(En), Mn = Xe(Dn), Nn = Ue;
(wn && Nn(new wn(/* @__PURE__ */ new ArrayBuffer(1))) != "[object DataView]" || ot && Nn(new ot()) != "[object Map]" || Tn && Nn(Tn.resolve()) != "[object Promise]" || En && Nn(new En()) != "[object Set]" || Dn && Nn(new Dn()) != "[object WeakMap]") && (Nn = function(e) {
	var t = Ue(e), n = t == "[object Object]" ? e.constructor : void 0, r = n ? Xe(n) : "";
	if (r) switch (r) {
		case On: return "[object DataView]";
		case kn: return "[object Map]";
		case An: return "[object Promise]";
		case jn: return "[object Set]";
		case Mn: return "[object WeakMap]";
	}
	return t;
});
var Pn = Nn, Fn = "[object Object]", In = Object.prototype.hasOwnProperty, Ln = function(e, t, n, r, i, a) {
	var o = F(e), s = F(t), c = o ? "[object Array]" : Pn(e), l = s ? "[object Array]" : Pn(t), u = (c = c == "[object Arguments]" ? Fn : c) == Fn, d = (l = l == "[object Arguments]" ? Fn : l) == Fn, f = c == l;
	if (f && nn(e)) {
		if (!nn(t)) return !1;
		o = !0, u = !1;
	}
	if (f && !u) return a ||= new Dt(), o || dn(e) ? Pt(e, t, n, r, i, a) : Bt(e, t, c, n, r, i, a);
	if (!(1 & n)) {
		var p = u && In.call(e, "__wrapped__"), m = d && In.call(t, "__wrapped__");
		if (p || m) {
			var h = p ? e.value() : e, g = m ? t.value() : t;
			return a ||= new Dt(), i(h, g, n, r, a);
		}
	}
	return !!f && (a ||= new Dt(), Cn(e, t, n, r, i, a));
}, Rn = function e(t, n, r, i, a) {
	return t === n || (t == null || n == null || !Yt(t) && !Yt(n) ? t != t && n != n : Ln(t, n, r, i, e, a));
}, zn = function(e, t, n, r) {
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
			var d = new Dt();
			if (r) var f = r(l, u, c, e, t, d);
			if (!(f === void 0 ? Rn(u, l, 3, r, d) : f)) return !1;
		}
	}
	return !0;
}, Bn = function(e) {
	return e == e && !We(e);
}, Vn = function(e) {
	for (var t = bn(e), n = t.length; n--;) {
		var r = t[n], i = e[r];
		t[n] = [
			r,
			i,
			Bn(i)
		];
	}
	return t;
}, Hn = function(e, t) {
	return function(n) {
		return n != null && n[e] === t && (t !== void 0 || e in Object(n));
	};
}, Un = function(e) {
	var t = Vn(e);
	return t.length == 1 && t[0][2] ? Hn(t[0][0], t[0][1]) : function(n) {
		return n === e || zn(n, e, t);
	};
}, Wn = function(e) {
	return typeof e == "symbol" || Yt(e) && Ue(e) == "[object Symbol]";
}, Gn = /\.|\[(?:[^[\]]*|(["'])(?:(?!\1)[^\\]|\\.)*?\1)\]/, Kn = /^\w*$/, qn = function(e, t) {
	if (F(e)) return !1;
	var n = typeof e;
	return !(n != "number" && n != "symbol" && n != "boolean" && e != null && !Wn(e)) || Kn.test(e) || !Gn.test(e) || t != null && e in Object(t);
};
function Jn(e, t) {
	if (typeof e != "function" || t != null && typeof t != "function") throw TypeError("Expected a function");
	var n = function() {
		var r = arguments, i = t ? t.apply(this, r) : r[0], a = n.cache;
		if (a.has(i)) return a.get(i);
		var o = e.apply(this, r);
		return n.cache = a.set(i, o) || a, o;
	};
	return n.cache = new (Jn.Cache || wt)(), n;
}
Jn.Cache = wt;
var Yn = Jn, Xn = /[^.[\]]+|\[(?:(-?\d+(?:\.\d+)?)|(["'])((?:(?!\2)[^\\]|\\.)*?)\2)\]|(?=(?:\.|\[\])(?:\.|\[\]|$))/g, Zn = /\\(\\)?/g, Qn = function(e) {
	var t = Yn(e, (function(e) {
		return n.size === 500 && n.clear(), e;
	})), n = t.cache;
	return t;
}((function(e) {
	var t = [];
	return e.charCodeAt(0) === 46 && t.push(""), e.replace(Xn, (function(e, n, r, i) {
		t.push(r ? i.replace(Zn, "$1") : n || e);
	})), t;
})), $n = function(e, t) {
	for (var n = -1, r = e == null ? 0 : e.length, i = Array(r); ++n < r;) i[n] = t(e[n], n, e);
	return i;
}, er = Pe ? Pe.prototype : void 0, tr = er ? er.toString : void 0, nr = function e(t) {
	if (typeof t == "string") return t;
	if (F(t)) return $n(t, e) + "";
	if (Wn(t)) return tr ? tr.call(t) : "";
	var n = t + "";
	return n == "0" && 1 / t == -Infinity ? "-0" : n;
}, rr = function(e) {
	return e == null ? "" : nr(e);
}, ir = function(e, t) {
	return F(e) ? e : qn(e, t) ? [e] : Qn(rr(e));
}, ar = function(e) {
	if (typeof e == "string" || Wn(e)) return e;
	var t = e + "";
	return t == "0" && 1 / e == -Infinity ? "-0" : t;
}, or = function(e, t) {
	for (var n = 0, r = (t = ir(t, e)).length; e != null && n < r;) e = e[ar(t[n++])];
	return n && n == r ? e : void 0;
}, sr = function(e, t, n) {
	var r = e == null ? void 0 : or(e, t);
	return r === void 0 ? n : r;
}, cr = function(e, t) {
	return e != null && t in Object(e);
}, lr = function(e, t, n) {
	for (var r = -1, i = (t = ir(t, e)).length, a = !1; ++r < i;) {
		var o = ar(t[r]);
		if (!(a = e != null && n(e, o))) break;
		e = e[o];
	}
	return a || ++r != i ? a : !!(i = e == null ? 0 : e.length) && on(i) && an(o, i) && (F(e) || en(e));
}, ur = function(e, t) {
	return e != null && lr(e, t, cr);
}, dr = function(e, t) {
	return qn(e) && Bn(t) ? Hn(ar(e), t) : function(n) {
		var r = sr(n, e);
		return r === void 0 && r === t ? ur(n, e) : Rn(t, r, 3);
	};
}, fr = function(e) {
	return e;
}, pr = function(e) {
	return function(t) {
		return t?.[e];
	};
}, mr = function(e) {
	return function(t) {
		return or(t, e);
	};
}, hr = function(e) {
	return qn(e) ? pr(ar(e)) : mr(e);
}, gr = function(e) {
	return typeof e == "function" ? e : e == null ? fr : typeof e == "object" ? F(e) ? dr(e[0], e[1]) : Un(e) : hr(e);
}, _r = /\s/, vr = function(e) {
	for (var t = e.length; t-- && _r.test(e.charAt(t)););
	return t;
}, yr = /^\s+/, br = function(e) {
	return e && e.slice(0, vr(e) + 1).replace(yr, "");
}, xr = /^[-+]0x[0-9a-f]+$/i, Sr = /^0b[01]+$/i, Cr = /^0o[0-7]+$/i, wr = parseInt, Tr = function(e) {
	if (typeof e == "number") return e;
	if (Wn(e)) return NaN;
	if (We(e)) {
		var t = typeof e.valueOf == "function" ? e.valueOf() : e;
		e = We(t) ? t + "" : t;
	}
	if (typeof e != "string") return e === 0 ? e : +e;
	e = br(e);
	var n = Sr.test(e);
	return n || Cr.test(e) ? wr(e.slice(2), n ? 2 : 8) : xr.test(e) ? NaN : +e;
}, Er = function(e) {
	return e ? (e = Tr(e)) === Infinity || e === -Infinity ? 17976931348623157e292 * (e < 0 ? -1 : 1) : e == e ? e : 0 : e === 0 ? e : 0;
}, Dr = function(e) {
	var t = Er(e), n = t % 1;
	return t == t ? n ? t - n : t : 0;
};
function Or(e) {
	if (!e) throw Error("change is not provided");
	return oe(e) ? `N${e.oldLineNumber}` : `${M(e) ? "I" : "D"}${e.lineNumber}`;
}
he("old");
var kr = de("old"), Ar = de("new");
me("old"), me("new"), he("new"), he("old");
var jr = function() {
	try {
		var e = at(Object, "defineProperty");
		return e({}, "", {}), e;
	} catch {}
}(), Mr = function(e, t, n) {
	t == "__proto__" && jr ? jr(e, t, {
		configurable: !0,
		enumerable: !0,
		value: n,
		writable: !0
	}) : e[t] = n;
}, Nr = function(e) {
	return function(t, n, r) {
		for (var i = -1, a = Object(t), o = r(t), s = o.length; s--;) {
			var c = o[e ? s : ++i];
			if (!1 === n(a[c], c, a)) break;
		}
		return t;
	};
}(), Pr = function(e, t) {
	return e && Nr(e, t, bn);
}, Fr = function(e, t) {
	var n = {};
	return t = gr(t), Pr(e, (function(e, r, i) {
		Mr(n, r, t(e, r, i));
	})), n;
}, Ir = [
	"changeKey",
	"text",
	"tokens",
	"renderToken"
], Lr = function e(t, n) {
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
			return l(k(s || u));
	}
};
function Rr(e) {
	if (!Array.isArray(e)) return !0;
	if (e.length > 1) return !1;
	if (e.length === 1) {
		var t = b(e, 1)[0];
		return t.type === "text" && !t.value;
	}
	return !0;
}
function zr(e) {
	var t = e.changeKey, n = e.text, r = e.tokens, i = e.renderToken, a = y(e, Ir), o = i ? function(e, t) {
		return i(e, Lr, t);
	} : Lr;
	return (0, m.jsx)("td", _(_({}, a), {}, {
		"data-change-key": t,
		children: r ? Rr(r) ? " " : r.map(o) : n || " "
	}));
}
var Br = (0, h.memo)(zr);
function Vr(e, t) {
	return function() {
		var n = t === "old" ? kr(e) : Ar(e);
		return n === -1 ? void 0 : n;
	};
}
function Hr(e, t) {
	return function(n) {
		return e && n ? (0, m.jsx)("a", {
			href: t ? "#" + t : void 0,
			children: n
		}) : n;
	};
}
function Ur(e, t) {
	return t ? function(n) {
		e(), t(n);
	} : e;
}
function Wr(e, t, n, r) {
	return (0, h.useMemo)((function() {
		var i = Fr(e, (function(e) {
			return function(n) {
				return e && e(t, n);
			};
		}));
		return i.onMouseEnter = Ur(n, i.onMouseEnter), i.onMouseLeave = Ur(r, i.onMouseLeave), i;
	}), [
		e,
		n,
		r,
		t
	]);
}
function Gr(e, t, n, r, i, a, o, s, c) {
	var l = {
		change: t,
		side: r,
		inHoverState: s,
		renderDefault: Vr(t, r),
		wrapInAnchor: Hr(i, a)
	};
	return (0, m.jsx)("td", _(_({ className: e }, o), {}, {
		"data-change-key": n,
		children: c(l)
	}));
}
function Kr(e) {
	var t, n, r, i = e.change, a = e.selected, o = e.tokens, s = e.className, c = e.generateLineClassName, l = e.gutterClassName, u = e.codeClassName, d = e.gutterEvents, f = e.codeEvents, p = e.hideGutter, g = e.gutterAnchor, v = e.generateAnchorID, y = e.renderToken, x = e.renderGutter, S = i.type, C = i.content, w = Or(i), T = b((t = b((0, h.useState)(!1), 2), n = t[0], r = t[1], [
		n,
		(0, h.useCallback)((function() {
			return r(!0);
		}), []),
		(0, h.useCallback)((function() {
			return r(!1);
		}), [])
	]), 3), E = T[0], ee = T[1], D = T[2], O = (0, h.useMemo)((function() {
		return { change: i };
	}), [i]), te = Wr(d, O, ee, D), A = Wr(f, O, ee, D), ne = v(i), re = c({
		changes: [i],
		defaultGenerate: function() {
			return s;
		}
	}), ie = k("diff-gutter", `diff-gutter-${S}`, l, { "diff-gutter-selected": a }), j = k("diff-code", `diff-code-${S}`, u, { "diff-code-selected": a });
	return (0, m.jsxs)("tr", {
		id: ne,
		className: k("diff-line", re),
		children: [
			!p && Gr(ie, i, w, "old", g, ne, te, E, x),
			!p && Gr(ie, i, w, "new", g, ne, te, E, x),
			(0, m.jsx)(Br, _({
				className: j,
				changeKey: w,
				text: C,
				tokens: o,
				renderToken: y
			}, A))
		]
	});
}
var qr = (0, h.memo)(Kr);
function Jr(e) {
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
var Yr = [
	"hideGutter",
	"selectedChanges",
	"tokens",
	"lineClassName"
], Xr = [
	"hunk",
	"widgets",
	"className"
];
function Zr(e) {
	var t = e.hunk, n = e.widgets, r = e.className, i = y(e, Xr), a = function(e, t) {
		return e.reduce((function(e, n) {
			var r = Or(n);
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
		className: k("diff-hunk", r),
		children: a.map((function(e) {
			return function(e, t) {
				var n = b(e, 3), r = n[0], i = n[1], a = n[2], o = t.hideGutter, s = t.selectedChanges, c = t.tokens, l = t.lineClassName, u = y(t, Yr);
				if (r === "change") {
					var d = ae(a) ? "old" : "new", f = ae(a) ? kr(a) : Ar(a), p = c ? c[d][f - 1] : null;
					return (0, m.jsx)(qr, _({
						className: l,
						change: a,
						hideGutter: o,
						selected: s.includes(i),
						tokens: p
					}, u), `change${i}`);
				}
				return r === "widget" ? (0, m.jsx)(Jr, {
					hideGutter: o,
					element: a
				}, `widget${i}`) : null;
			}(e, i);
		}))
	});
}
var Qr = 0;
function $r(e, t, n, r) {
	var i = (0, h.useCallback)((function() {
		return t(e);
	}), [e, t]), a = (0, h.useCallback)((function() {
		return t("");
	}), [t]);
	return (0, h.useMemo)((function() {
		var t = Fr(r, (function(t) {
			return function(r) {
				return t && t({
					side: e,
					change: n
				}, r);
			};
		}));
		return t.onMouseEnter = Ur(i, t.onMouseEnter), t.onMouseLeave = Ur(a, t.onMouseLeave), t;
	}), [
		n,
		r,
		i,
		e,
		a
	]);
}
function ei(e) {
	var t = e.change, n = e.side, r = e.selected, i = e.tokens, a = e.gutterClassName, o = e.codeClassName, s = e.gutterEvents, c = e.codeEvents, l = e.anchorID, u = e.gutterAnchor, d = e.gutterAnchorTarget, f = e.hideGutter, p = e.hover, h = e.renderToken, g = e.renderGutter;
	if (!t) {
		var y = k("diff-gutter", "diff-gutter-omit", a), b = k("diff-code", "diff-code-omit", o);
		return [!f && (0, m.jsx)("td", { className: y }, "gutter"), (0, m.jsx)("td", { className: b }, "code")];
	}
	var x = t.type, S = t.content, C = Or(t), w = n === Qr ? "old" : "new", T = _({
		id: l || void 0,
		className: k("diff-gutter", `diff-gutter-${x}`, v({ "diff-gutter-selected": r }, "diff-line-hover-" + w, p), a),
		children: g({
			change: t,
			side: w,
			inHoverState: p,
			renderDefault: Vr(t, w),
			wrapInAnchor: Hr(u, d)
		})
	}, s), E = k("diff-code", `diff-code-${x}`, v({ "diff-code-selected": r }, "diff-line-hover-" + w, p), o);
	return [!f && (0, m.jsx)("td", _(_({}, T), {}, { "data-change-key": C }), "gutter"), (0, m.jsx)(Br, _({
		className: E,
		changeKey: C,
		text: S,
		tokens: i,
		renderToken: h
	}, c), "code")];
}
function ti(e) {
	var t = e.className, n = e.oldChange, r = e.newChange, i = e.oldSelected, a = e.newSelected, o = e.oldTokens, s = e.newTokens, c = e.monotonous, l = e.gutterClassName, u = e.codeClassName, d = e.gutterEvents, f = e.codeEvents, p = e.hideGutter, g = e.generateAnchorID, v = e.generateLineClassName, y = e.gutterAnchor, x = e.renderToken, S = e.renderGutter, C = b((0, h.useState)(""), 2), w = C[0], T = C[1], E = $r("old", T, n, d), ee = $r("new", T, r, d), D = $r("old", T, n, f), O = $r("new", T, r, f), te = n && g(n), A = r && g(r), ne = v({
		changes: [n, r],
		defaultGenerate: function() {
			return t;
		}
	}), re = {
		monotonous: c,
		hideGutter: p,
		gutterClassName: l,
		codeClassName: u,
		gutterEvents: d,
		codeEvents: f,
		renderToken: x,
		renderGutter: S
	}, ie = _(_({}, re), {}, {
		change: n,
		side: Qr,
		selected: i,
		tokens: o,
		gutterEvents: E,
		codeEvents: D,
		anchorID: te,
		gutterAnchor: y,
		gutterAnchorTarget: te,
		hover: w === "old"
	}), j = _(_({}, re), {}, {
		change: r,
		side: 1,
		selected: a,
		tokens: s,
		gutterEvents: ee,
		codeEvents: O,
		anchorID: n === r ? null : A,
		gutterAnchor: y,
		gutterAnchorTarget: n === r ? te : A,
		hover: w === "new"
	});
	return c ? (0, m.jsx)("tr", {
		className: k("diff-line", ne),
		children: ei(n ? ie : j)
	}) : (0, m.jsxs)("tr", {
		className: k("diff-line", function(e, t) {
			return e && !t ? "diff-line-old-only" : !e && t ? "diff-line-new-only" : e === t ? "diff-line-normal" : "diff-line-compare";
		}(n, r), ne),
		children: [ei(ie), ei(j)]
	});
}
var ni = (0, h.memo)(ti);
function ri(e) {
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
var ii = [
	"selectedChanges",
	"monotonous",
	"hideGutter",
	"tokens",
	"lineClassName"
], ai = [
	"hunk",
	"widgets",
	"className"
];
function oi(e, t) {
	return (e ? Or(e) : "00") + (t ? Or(t) : "00");
}
function si(e) {
	var t = e.hunk, n = e.widgets, r = e.className, i = y(e, ai), a = function(e, t) {
		for (var n = function(e) {
			return e && t[Or(e)] || null;
		}, r = [], i = 0; i < e.length; i++) {
			var a = e[i];
			if (oe(a)) r.push([
				"change",
				oi(a, a),
				a,
				a
			]);
			else if (ae(a)) {
				var o = e[i + 1];
				o && M(o) ? (i += 1, r.push([
					"change",
					oi(a, o),
					a,
					o
				])) : r.push([
					"change",
					oi(a, null),
					a,
					null
				]);
			} else r.push([
				"change",
				oi(null, a),
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
		className: k("diff-hunk", r),
		children: a.map((function(e) {
			return function(e, t) {
				var n = b(e, 4), r = n[0], i = n[1], a = n[2], o = n[3], s = t.selectedChanges, c = t.monotonous, l = t.hideGutter, u = t.tokens, d = t.lineClassName, f = y(t, ii);
				if (r === "change") {
					var p = !!a && s.includes(Or(a)), h = !!o && s.includes(Or(o)), g = a && u ? u.old[kr(a) - 1] : null, v = o && u ? u.new[Ar(o) - 1] : null;
					return (0, m.jsx)(ni, _({
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
				return r === "widget" ? (0, m.jsx)(ri, {
					monotonous: c,
					hideGutter: l,
					oldElement: a,
					newElement: o
				}, `widget${i}`) : null;
			}(e, i);
		}))
	});
}
var ci = ["gutterType", "hunkClassName"];
function li(e) {
	var t = e.hunk, n = ie(), r = n.gutterType, i = n.hunkClassName, a = y(n, ci), o = r === "none", s = r === "anchor", c = a.viewType === "unified" ? Zr : si;
	return (0, m.jsx)(c, _(_({}, a), {}, {
		hunk: t,
		hideGutter: o,
		gutterAnchor: s,
		className: i
	}));
}
function ui() {}
function di(e, t) {
	var n = t ? "auto" : "none";
	e instanceof HTMLElement && e.style.userSelect !== n && (e.style.userSelect = n);
}
function fi(e) {
	return e.map((function(e) {
		return (0, m.jsx)(li, { hunk: e }, function(e) {
			return `-${e.oldStart},${e.oldLines} +${e.newStart},${e.newLines}`;
		}(e));
	}));
}
function pi(e) {
	var t = e.diffType, n = e.hunks, r = e.optimizeSelection, i = e.className, a = e.hunkClassName, o = a === void 0 ? A.hunkClassName : a, s = e.lineClassName, c = s === void 0 ? A.lineClassName : s, l = e.generateLineClassName, u = l === void 0 ? A.generateLineClassName : l, d = e.gutterClassName, f = d === void 0 ? A.gutterClassName : d, p = e.codeClassName, g = p === void 0 ? A.codeClassName : p, _ = e.gutterType, v = _ === void 0 ? A.gutterType : _, y = e.viewType, b = y === void 0 ? A.viewType : y, x = e.gutterEvents, C = x === void 0 ? A.gutterEvents : x, w = e.codeEvents, T = w === void 0 ? A.codeEvents : w, E = e.generateAnchorID, ee = E === void 0 ? A.generateAnchorID : E, O = e.selectedChanges, te = O === void 0 ? A.selectedChanges : O, ne = e.widgets, ie = ne === void 0 ? A.widgets : ne, j = e.renderGutter, M = j === void 0 ? A.renderGutter : j, ae = e.tokens, oe = e.renderToken, se = e.children, ce = se === void 0 ? fi : se, le = (0, h.useRef)(null), N = (0, h.useCallback)((function(e) {
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
					var a, o = D(le.current ? le.current.querySelectorAll(".diff-line") : []);
					try {
						for (o.s(); !(a = o.n()).done;) {
							var s = a.value.children;
							di(s[1], i === 1), di(s[3], i === 3);
						}
					} catch (e) {
						o.e(e);
					} finally {
						o.f();
					}
				}
			}
		}
	}), []), ue = v === "none", de = t === "add" || t === "delete", fe = b === "split" && !de && r ? N : ui, pe = (0, h.useMemo)((function() {
		return (0, m.jsxs)("colgroup", b === "unified" ? { children: [
			!ue && (0, m.jsx)("col", { className: "diff-gutter-col" }),
			!ue && (0, m.jsx)("col", { className: "diff-gutter-col" }),
			(0, m.jsx)("col", {})
		] } : de ? { children: [!ue && (0, m.jsx)("col", { className: "diff-gutter-col" }), (0, m.jsx)("col", {})] } : { children: [
			!ue && (0, m.jsx)("col", { className: "diff-gutter-col" }),
			(0, m.jsx)("col", {}),
			!ue && (0, m.jsx)("col", { className: "diff-gutter-col" }),
			(0, m.jsx)("col", {})
		] });
	}), [
		b,
		de,
		ue
	]), me = (0, h.useMemo)((function() {
		return {
			hunkClassName: o,
			lineClassName: c,
			generateLineClassName: u,
			gutterClassName: f,
			codeClassName: g,
			monotonous: de,
			hideGutter: ue,
			viewType: b,
			gutterType: v,
			codeEvents: T,
			gutterEvents: C,
			generateAnchorID: ee,
			selectedChanges: te,
			widgets: ie,
			renderGutter: M,
			tokens: ae,
			renderToken: oe
		};
	}), [
		g,
		T,
		ee,
		f,
		C,
		v,
		ue,
		o,
		c,
		u,
		de,
		M,
		oe,
		te,
		ae,
		b,
		ie
	]);
	return (0, m.jsx)(re, {
		value: me,
		children: (0, m.jsxs)("table", {
			ref: le,
			className: k("diff", `diff-${b}`, i),
			onMouseDown: fe,
			children: [pe, ce(n)]
		})
	});
}
var mi = (0, h.memo)(pi), hi = function(e, t, n, r) {
	for (var i = -1, a = e == null ? 0 : e.length; ++i < a;) {
		var o = e[i];
		t(r, o, n(o), e);
	}
	return r;
}, gi = function(e, t) {
	return function(n, r) {
		if (n == null) return n;
		if (!yn(n)) return e(n, r);
		for (var i = n.length, a = t ? i : -1, o = Object(n); (t ? a-- : ++a < i) && !1 !== r(o[a], a, o););
		return n;
	};
}(Pr), _i = function(e, t, n, r) {
	return gi(e, (function(e, i, a) {
		t(r, e, n(e), a);
	})), r;
}, vi = function(e, t) {
	return function(n, r) {
		var i = F(n) ? hi : _i, a = t ? t() : {};
		return i(n, e, gr(r), a);
	};
}, yi = vi((function(e, t, n) {
	Mr(e, n, t);
})), bi = Pe ? Pe.isConcatSpreadable : void 0, xi = function(e) {
	return F(e) || en(e) || !!(bi && e && e[bi]);
}, Si = function e(t, n, r, i, a) {
	var o = -1, s = t.length;
	for (r ||= xi, a ||= []; ++o < s;) {
		var c = t[o];
		n > 0 && r(c) ? n > 1 ? e(c, n - 1, r, i, a) : Vt(a, c) : i || (a[a.length] = c);
	}
	return a;
}, Ci = function(e, t) {
	var n = -1, r = yn(e) ? Array(e.length) : [];
	return gi(e, (function(e, i, a) {
		r[++n] = t(e, i, a);
	})), r;
}, wi = function(e, t) {
	return (F(e) ? $n : Ci)(e, gr(t));
}, Ti = function(e, t) {
	return Si(wi(e, t), 1);
};
function Ei(e, t) {
	var n = t.newStart;
	return b(t.changes.reduce((function(e, t) {
		var n = b(e, 2), r = n[0], i = n[1];
		return ae(t) ? (r.splice(i, 1), [r, i]) : (M(t) && r.splice(i, 0, t.content), [r, i + 1]);
	}), [e, n - 1]), 1)[0];
}
function Di(e, t, n) {
	if (!e.length) return [];
	var r = t === "old" ? kr : Ar, i = yi(e, r), a = r(e[e.length - 1]);
	return Array.from({ length: a }).map((function(e, t) {
		return n(i[t + 1]);
	}));
}
function Oi(e) {
	var t = b(function(e) {
		return Ti(e, (function(e) {
			return e.changes;
		})).reduce((function(e, t) {
			var n = b(e, 2), r = n[0], i = n[1];
			return oe(t) ? (r.push(t), i.push(t)) : ae(t) ? r.push(t) : i.push(t), [r, i];
		}), [[], []]);
	}(e), 2), n = t[0], r = t[1], i = function(e) {
		return e ? e.content : "";
	};
	return [Di(n, "old", i).join("\n"), Di(r, "new", i).join("\n")];
}
function ki(e) {
	return {
		type: "root",
		children: e
	};
}
function Ai(e, t) {
	if (t.oldSource) {
		var n = function(e, t) {
			return t.reduce(Ei, e.split("\n")).join("\n");
		}(t.oldSource, e), r = t.highlight ? function(e) {
			return t.refractor.highlight(e, t.language);
		} : function(e) {
			return [{
				type: "text",
				value: e
			}];
		};
		return [ki(r(t.oldSource)), ki(r(n))];
	}
	var i = b(Oi(e), 2), a = i[0], o = i[1], s = t.highlight ? function(e) {
		return ki(t.refractor.highlight(e, t.language));
	} : function(e) {
		return ki([{
			type: "text",
			value: e
		}]);
	};
	return [s(a), s(o)];
}
function ji(e) {
	return e.map((function(e) {
		return _({}, e);
	}));
}
function Mi(e, t) {
	return [].concat(S(ji(e.slice(0, -1))), [t]);
}
function Ni(e) {
	return e.type === "text";
}
function Pi(e) {
	var t = e[e.length - 1];
	if (Ni(t)) return t;
	throw Error(`Invalid token path with leaf of type ${t.type}`);
}
function Fi(e, t, n, r) {
	var i = e.slice(0, -1), a = Pi(e), o = [];
	if (n <= 0 || t >= a?.value.length) return [e];
	var s = function(e, t) {
		var n = a.value.slice(e, t);
		return [].concat(S(i), [_(_({}, a), {}, { value: n })]);
	};
	if (t > 0) {
		var c = s(0, t);
		o.push(ji(c));
	}
	var l = s(Math.max(t, 0), n);
	if (o.push(r ? function(e, t) {
		return [t].concat(S(ji(e)));
	}(l, r) : ji(l)), n < a.value.length) {
		var u = s(n);
		o.push(ji(u));
	}
	return o;
}
var L = ["children"];
function R(e) {
	var t = arguments.length > 1 && arguments[1] !== void 0 ? arguments[1] : [], n = arguments.length > 2 && arguments[2] !== void 0 ? arguments[2] : [];
	if (e.children) {
		var r = e.children, i = y(e, L);
		n.push(i);
		var a, o = D(r);
		try {
			for (o.s(); !(a = o.n()).done;) R(a.value, t, n);
		} catch (e) {
			o.e(e);
		} finally {
			o.f();
		}
		n.pop();
	} else t.push(ji([].concat(S(n.slice(1)), [e])));
	return t;
}
function Ii(e) {
	return e.reduce((function(e, t) {
		var n = e[e.length - 1], r = x(function(e) {
			var t = Pi(e);
			return t.value.includes("\n") ? t.value.split("\n").map((function(n) {
				return Mi(e, _(_({}, t), {}, { value: n }));
			})) : [e];
		}(t)), i = r[0], a = r.slice(1);
		return [].concat(S(e.slice(0, -1)), [[].concat(S(n), [i])], S(a.map((function(e) {
			return [e];
		}))));
	}), [[]]);
}
function Li(e) {
	return Ii(R(e));
}
var Ri = function(e, t, n) {
	var r = (n = typeof n == "function" ? n : void 0) ? n(e, t) : void 0;
	return r === void 0 ? Rn(e, t, void 0, n) : !!r;
}, zi = function(e, t) {
	return Rn(e, t);
}, Bi = function(e) {
	var t = e == null ? 0 : e.length;
	return t ? e[t - 1] : void 0;
};
function Vi(e, t) {
	if (!e.children) throw Error("parent node missing children property");
	var n, r, i = Bi(e.children);
	return i && (r = t, (n = i).type === r.type && (n.type === "text" || n.children && r.children && Ri(n, r, (function(e, t, n) {
		return n === "chlidren" || zi(e, t);
	})))) ? e.children[e.children.length - 1] = function(e, t) {
		return "value" in e && "value" in t ? _(_({}, e), {}, { value: `${e.value}${t.value}` }) : e;
	}(i, t) : e.children.push(t), e.children[e.children.length - 1];
}
function Hi(e) {
	var t, n = {
		type: "root",
		children: []
	}, r = D(e);
	try {
		var i = function() {
			var e = t.value;
			e.reduce((function(t, n, r) {
				return Vi(t, r === e.length - 1 ? _({}, n) : _(_({}, n), {}, { children: [] }));
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
var Ui = Object.prototype.hasOwnProperty, Wi = vi((function(e, t, n) {
	Ui.call(e, n) ? e[n].push(t) : Mr(e, n, [t]);
})), Gi = Object.prototype.hasOwnProperty, Ki = function(e) {
	if (e == null) return !0;
	if (yn(e) && (F(e) || typeof e == "string" || typeof e.splice == "function" || nn(e) || dn(e) || en(e))) return !e.length;
	var t = Pn(e);
	if (t == "[object Map]" || t == "[object Set]") return !e.size;
	if (hn(e)) return !vn(e).length;
	for (var n in e) if (Gi.call(e, n)) return !1;
	return !0;
}, qi = function(e, t) {
	var n = t.start, r = n + t.length;
	return b(e.reduce((function(e, i) {
		var a = b(e, 2), o = a[0], s = a[1], c = s + Pi(i).value.length;
		if (s > r || c < n) o.push(i);
		else {
			var l = Fi(i, n - s, r - s, t);
			o.push.apply(o, S(l));
		}
		return [o, c];
	}), [[], 0]), 1)[0];
};
function Ji(e, t) {
	var n = Wi(t, "lineNumber");
	return e.map((function(e, t) {
		return function(e, t) {
			return Ki(t) ? e : t.reduce(qi, e);
		}(e, n[t + 1]);
	}));
}
function Yi(e, t) {
	return function(n) {
		var r = b(n, 2), i = r[0], a = r[1];
		return [Ji(i, e), Ji(a, t)];
	};
}
var Xi = function(e) {
	return e != null && e.length ? Si(e, 1) : [];
}, Zi = Math.max, Qi = function(e, t, n) {
	var r = e == null ? 0 : e.length;
	if (!r) return -1;
	var i = n == null ? 0 : Dr(n);
	return i < 0 && (i = Zi(r + i, 0)), ge(e, gr(t), i);
}, $i = te((function(e) {
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
})), ea = $i.DIFF_EQUAL, ta = $i.DIFF_DELETE, na = $i.DIFF_INSERT;
function ra(e) {
	var t = Qi(e, (function(e) {
		return !oe(e);
	}));
	if (t === -1) return [];
	var n = Qi(e, (function(e) {
		return !!oe(e);
	}), t);
	return n === -1 ? [e.slice(t)] : [e.slice(t, n)].concat(S(ra(e.slice(n))));
}
function ia(e) {
	return e.reduce((function(e, t) {
		var n = b(t, 2), r = n[0], i = x(n[1].split("\n").map((function(e) {
			return [r, e];
		}))), a = i[0], o = i.slice(1);
		return [].concat(S(e.slice(0, -1)), [[].concat(S(e[e.length - 1]), [a])], S(o.map((function(e) {
			return [e];
		}))));
	}), [[]]);
}
function aa(e, t) {
	return e.reduce((function(e, n) {
		var r = b(e, 2), i = r[0], a = r[1], o = b(n, 2), s = o[0], c = o[1];
		if (s !== ea) {
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
function oa(e, t) {
	return Ti(e, (function(e, n) {
		return aa(e, t + n);
	}));
}
function sa(e, t) {
	var n = new $i(), r = n.diff_main(e, t);
	return n.diff_cleanupSemantic(r), r.length <= 1 ? [[], []] : function(e) {
		return e.reduce((function(e, t) {
			var n = b(e, 2), r = n[0], i = n[1];
			switch (b(t, 1)[0]) {
				case na:
					i.push(t);
					break;
				case ta:
					r.push(t);
					break;
				default: r.push(t), i.push(t);
			}
			return [r, i];
		}), [[], []]);
	}(r);
}
function ca(e) {
	var t = b(e.reduce((function(e, t) {
		var n = b(e, 2), r = n[0], i = n[1];
		return ae(t) ? [r + (r ? "\n" : "") + t.content, i] : [r, i + (i ? "\n" : "") + t.content];
	}), ["", ""]), 2), n = b(sa(t[0], t[1]), 2), r = n[0], i = n[1];
	if (r.length === 0 && i.length === 0) return [[], []];
	var a = function(e) {
		if (e && !oe(e)) return e.lineNumber;
	}, o = a(e.find(ae)), s = a(e.find(M));
	if (o === void 0 || s === void 0) throw Error("Could not find start line number for edit");
	return [oa(ia(r), o), oa(ia(i), s)];
}
function la(e) {
	var t = b(e.reduce((function(e, t) {
		var n = b(e, 3), r = n[0], i = n[1], a = n[2];
		if (!a || !ae(a) || !M(t)) return [
			r,
			i,
			t
		];
		var o = b(sa(a.content, t.content), 2), s = o[0], c = o[1];
		return [
			r.concat(aa(s, a.lineNumber)),
			i.concat(aa(c, t.lineNumber)),
			t
		];
	}), [
		[],
		[],
		null
	]), 2);
	return [t[0], t[1]];
}
function ua(e) {
	var t = (arguments.length > 1 && arguments[1] !== void 0 ? arguments[1] : {}).type, n = (t === void 0 ? "block" : t) === "block" ? ca : la, r = b(Ti(e.map((function(e) {
		return e.changes;
	})), ra).map(n).reduce((function(e, t) {
		var n = b(e, 2), r = n[0], i = n[1], a = b(t, 2), o = a[0], s = a[1];
		return [r.concat(o), i.concat(s)];
	}), [[], []]), 2), i = r[0], a = r[1];
	return Yi(Xi(i), Xi(a));
}
var da = ["enhancers"], fa = function(e) {
	var t, n = arguments.length > 1 && arguments[1] !== void 0 ? arguments[1] : {}, r = n.enhancers, i = r === void 0 ? [] : r, a = b(Ai(e, y(n, da)), 2), o = a[0], s = a[1], c = [Li(o), Li(s)], l = b((t = [c[0], c[1]], i.reduce((function(e, t) {
		return t(e);
	}), t)), 2), u = l[0], d = l[1], f = [u.map(Hi), d.map(Hi)], p = f[1];
	return {
		old: f[0].map((function(e) {
			return e.children ?? [];
		})),
		new: p.map((function(e) {
			return e.children ?? [];
		}))
	};
}, pa = class {
	constructor(e, t, n) {
		this.normal = t, this.property = e, n && (this.space = n);
	}
};
pa.prototype.normal = {}, pa.prototype.property = {}, pa.prototype.space = void 0;
//#endregion
//#region node_modules/property-information/lib/util/merge.js
function ma(e, t) {
	let n = {}, r = {};
	for (let t of e) Object.assign(n, t.property), Object.assign(r, t.normal);
	return new pa(n, r, t);
}
//#endregion
//#region node_modules/property-information/lib/normalize.js
function ha(e) {
	return e.toLowerCase();
}
//#endregion
//#region node_modules/property-information/lib/util/info.js
var ga = class {
	constructor(e, t) {
		this.attribute = t, this.property = e;
	}
};
ga.prototype.attribute = "", ga.prototype.booleanish = !1, ga.prototype.boolean = !1, ga.prototype.commaOrSpaceSeparated = !1, ga.prototype.commaSeparated = !1, ga.prototype.defined = !1, ga.prototype.mustUseProperty = !1, ga.prototype.number = !1, ga.prototype.overloadedBoolean = !1, ga.prototype.property = "", ga.prototype.spaceSeparated = !1, ga.prototype.space = void 0;
//#endregion
//#region node_modules/property-information/lib/util/types.js
var _a = /* @__PURE__ */ n({
	boolean: () => z,
	booleanish: () => ya,
	commaOrSpaceSeparated: () => Sa,
	commaSeparated: () => xa,
	number: () => B,
	overloadedBoolean: () => ba,
	spaceSeparated: () => V
}), va = 0, z = Ca(), ya = Ca(), ba = Ca(), B = Ca(), V = Ca(), xa = Ca(), Sa = Ca();
function Ca() {
	return 2 ** ++va;
}
//#endregion
//#region node_modules/property-information/lib/util/defined-info.js
var wa = Object.keys(_a), Ta = class extends ga {
	constructor(e, t, n, r) {
		let i = -1;
		if (super(e, t), Ea(this, "space", r), typeof n == "number") for (; ++i < wa.length;) {
			let e = wa[i];
			Ea(this, wa[i], (n & _a[e]) === _a[e]);
		}
	}
};
Ta.prototype.defined = !0;
function Ea(e, t, n) {
	n && (e[t] = n);
}
//#endregion
//#region node_modules/property-information/lib/util/create.js
function Da(e) {
	let t = {}, n = {};
	for (let [r, i] of Object.entries(e.properties)) {
		let a = new Ta(r, e.transform(e.attributes || {}, r), i, e.space);
		e.mustUseProperty && e.mustUseProperty.includes(r) && (a.mustUseProperty = !0), t[r] = a, n[ha(r)] = r, n[ha(a.attribute)] = r;
	}
	return new pa(t, n, e.space);
}
//#endregion
//#region node_modules/property-information/lib/aria.js
var Oa = Da({
	properties: {
		ariaActiveDescendant: null,
		ariaAtomic: ya,
		ariaAutoComplete: null,
		ariaBusy: ya,
		ariaChecked: ya,
		ariaColCount: B,
		ariaColIndex: B,
		ariaColSpan: B,
		ariaControls: V,
		ariaCurrent: null,
		ariaDescribedBy: V,
		ariaDetails: null,
		ariaDisabled: ya,
		ariaDropEffect: V,
		ariaErrorMessage: null,
		ariaExpanded: ya,
		ariaFlowTo: V,
		ariaGrabbed: ya,
		ariaHasPopup: null,
		ariaHidden: ya,
		ariaInvalid: null,
		ariaKeyShortcuts: null,
		ariaLabel: null,
		ariaLabelledBy: V,
		ariaLevel: B,
		ariaLive: null,
		ariaModal: ya,
		ariaMultiLine: ya,
		ariaMultiSelectable: ya,
		ariaOrientation: null,
		ariaOwns: V,
		ariaPlaceholder: null,
		ariaPosInSet: B,
		ariaPressed: ya,
		ariaReadOnly: ya,
		ariaRelevant: null,
		ariaRequired: ya,
		ariaRoleDescription: V,
		ariaRowCount: B,
		ariaRowIndex: B,
		ariaRowSpan: B,
		ariaSelected: ya,
		ariaSetSize: B,
		ariaSort: null,
		ariaValueMax: B,
		ariaValueMin: B,
		ariaValueNow: B,
		ariaValueText: null,
		role: null
	},
	transform(e, t) {
		return t === "role" ? t : "aria-" + t.slice(4).toLowerCase();
	}
});
//#endregion
//#region node_modules/property-information/lib/util/case-sensitive-transform.js
function ka(e, t) {
	return t in e ? e[t] : t;
}
//#endregion
//#region node_modules/property-information/lib/util/case-insensitive-transform.js
function Aa(e, t) {
	return ka(e, t.toLowerCase());
}
//#endregion
//#region node_modules/property-information/lib/html.js
var ja = Da({
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
		accept: xa,
		acceptCharset: V,
		accessKey: V,
		action: null,
		allow: null,
		allowFullScreen: z,
		allowPaymentRequest: z,
		allowUserMedia: z,
		alpha: z,
		alt: null,
		as: null,
		async: z,
		autoCapitalize: null,
		autoComplete: V,
		autoFocus: z,
		autoPlay: z,
		blocking: V,
		capture: null,
		charSet: null,
		checked: z,
		cite: null,
		className: V,
		closedBy: null,
		colorSpace: null,
		cols: B,
		colSpan: B,
		command: null,
		commandFor: null,
		content: null,
		contentEditable: ya,
		controls: z,
		controlsList: V,
		coords: B | xa,
		crossOrigin: null,
		data: null,
		dateTime: null,
		decoding: null,
		default: z,
		defer: z,
		dir: null,
		dirName: null,
		disabled: z,
		download: ba,
		draggable: ya,
		encType: null,
		enterKeyHint: null,
		fetchPriority: null,
		form: null,
		formAction: null,
		formEncType: null,
		formMethod: null,
		formNoValidate: z,
		formTarget: null,
		headers: V,
		height: B,
		hidden: ba,
		high: B,
		href: null,
		hrefLang: null,
		htmlFor: V,
		httpEquiv: V,
		id: null,
		imageSizes: null,
		imageSrcSet: null,
		inert: z,
		inputMode: null,
		integrity: null,
		is: null,
		isMap: z,
		itemId: null,
		itemProp: V,
		itemRef: V,
		itemScope: z,
		itemType: V,
		kind: null,
		label: null,
		lang: null,
		language: null,
		list: null,
		loading: null,
		loop: z,
		low: B,
		manifest: null,
		max: null,
		maxLength: B,
		media: null,
		method: null,
		min: null,
		minLength: B,
		multiple: z,
		muted: z,
		name: null,
		nonce: null,
		noModule: z,
		noValidate: z,
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
		open: z,
		optimum: B,
		pattern: null,
		ping: V,
		placeholder: null,
		playsInline: z,
		popover: null,
		popoverTarget: null,
		popoverTargetAction: null,
		poster: null,
		preload: null,
		readOnly: z,
		referrerPolicy: null,
		rel: V,
		required: z,
		reversed: z,
		rows: B,
		rowSpan: B,
		sandbox: V,
		scope: null,
		scoped: z,
		seamless: z,
		selected: z,
		shadowRootClonable: z,
		shadowRootCustomElementRegistry: z,
		shadowRootDelegatesFocus: z,
		shadowRootMode: null,
		shadowRootSerializable: z,
		shape: null,
		size: B,
		sizes: null,
		slot: null,
		span: B,
		spellCheck: ya,
		src: null,
		srcDoc: null,
		srcLang: null,
		srcSet: null,
		start: B,
		step: null,
		style: null,
		tabIndex: B,
		target: null,
		title: null,
		translate: null,
		type: null,
		typeMustMatch: z,
		useMap: null,
		value: ya,
		width: B,
		wrap: null,
		writingSuggestions: null,
		align: null,
		aLink: null,
		archive: V,
		axis: null,
		background: null,
		bgColor: null,
		border: B,
		borderColor: null,
		bottomMargin: B,
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
		compact: z,
		declare: z,
		event: null,
		face: null,
		frame: null,
		frameBorder: null,
		hSpace: B,
		leftMargin: B,
		link: null,
		longDesc: null,
		lowSrc: null,
		marginHeight: B,
		marginWidth: B,
		noResize: z,
		noHref: z,
		noShade: z,
		noWrap: z,
		object: null,
		profile: null,
		prompt: null,
		rev: null,
		rightMargin: B,
		rules: null,
		scheme: null,
		scrolling: ya,
		standby: null,
		summary: null,
		text: null,
		topMargin: B,
		valueType: null,
		version: null,
		vAlign: null,
		vLink: null,
		vSpace: B,
		allowTransparency: null,
		autoCorrect: null,
		autoSave: null,
		credentialless: z,
		disablePictureInPicture: z,
		disableRemotePlayback: z,
		exportParts: xa,
		part: V,
		prefix: null,
		property: null,
		results: B,
		security: null,
		unselectable: null
	},
	space: "html",
	transform: Aa
}), Ma = Da({
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
		about: Sa,
		accentHeight: B,
		accumulate: null,
		additive: null,
		alignmentBaseline: null,
		alphabetic: B,
		amplitude: B,
		arabicForm: null,
		ascent: B,
		attributeName: null,
		attributeType: null,
		azimuth: B,
		bandwidth: null,
		baselineShift: null,
		baseFrequency: null,
		baseProfile: null,
		bbox: null,
		begin: null,
		bias: B,
		by: null,
		calcMode: null,
		capHeight: B,
		className: V,
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
		descent: B,
		diffuseConstant: B,
		direction: null,
		display: null,
		dur: null,
		divisor: B,
		dominantBaseline: null,
		download: z,
		dx: null,
		dy: null,
		edgeMode: null,
		editable: null,
		elevation: B,
		enableBackground: null,
		end: null,
		event: null,
		exponent: B,
		externalResourcesRequired: null,
		fill: null,
		fillOpacity: B,
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
		g1: xa,
		g2: xa,
		glyphName: xa,
		glyphOrientationHorizontal: null,
		glyphOrientationVertical: null,
		glyphRef: null,
		gradientTransform: null,
		gradientUnits: null,
		handler: null,
		hanging: B,
		hatchContentUnits: null,
		hatchUnits: null,
		height: null,
		href: null,
		hrefLang: null,
		horizAdvX: B,
		horizOriginX: B,
		horizOriginY: B,
		id: null,
		ideographic: B,
		imageRendering: null,
		initialVisibility: null,
		in: null,
		in2: null,
		intercept: B,
		k: B,
		k1: B,
		k2: B,
		k3: B,
		k4: B,
		kernelMatrix: Sa,
		kernelUnitLength: null,
		keyPoints: null,
		keySplines: null,
		keyTimes: null,
		kerning: null,
		lang: null,
		lengthAdjust: null,
		letterSpacing: null,
		lightingColor: null,
		limitingConeAngle: B,
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
		mediaSize: B,
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
		overlinePosition: B,
		overlineThickness: B,
		paintOrder: null,
		panose1: null,
		path: null,
		pathLength: B,
		patternContentUnits: null,
		patternTransform: null,
		patternUnits: null,
		phase: null,
		ping: V,
		pitch: null,
		playbackOrder: null,
		pointerEvents: null,
		points: null,
		pointsAtX: B,
		pointsAtY: B,
		pointsAtZ: B,
		preserveAlpha: null,
		preserveAspectRatio: null,
		primitiveUnits: null,
		propagate: null,
		property: Sa,
		r: null,
		radius: null,
		referrerPolicy: null,
		refX: null,
		refY: null,
		rel: Sa,
		rev: Sa,
		renderingIntent: null,
		repeatCount: null,
		repeatDur: null,
		requiredExtensions: Sa,
		requiredFeatures: Sa,
		requiredFonts: Sa,
		requiredFormats: Sa,
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
		specularConstant: B,
		specularExponent: B,
		spreadMethod: null,
		spacing: null,
		startOffset: null,
		stdDeviation: null,
		stemh: null,
		stemv: null,
		stitchTiles: null,
		stopColor: null,
		stopOpacity: null,
		strikethroughPosition: B,
		strikethroughThickness: B,
		string: null,
		stroke: null,
		strokeDashArray: Sa,
		strokeDashOffset: null,
		strokeLineCap: null,
		strokeLineJoin: null,
		strokeMiterLimit: B,
		strokeOpacity: B,
		strokeWidth: null,
		style: null,
		surfaceScale: B,
		syncBehavior: null,
		syncBehaviorDefault: null,
		syncMaster: null,
		syncTolerance: null,
		syncToleranceDefault: null,
		systemLanguage: Sa,
		tabIndex: B,
		tableValues: null,
		target: null,
		targetX: B,
		targetY: B,
		textAnchor: null,
		textDecoration: null,
		textRendering: null,
		textLength: null,
		timelineBegin: null,
		title: null,
		transformBehavior: null,
		type: null,
		typeOf: Sa,
		to: null,
		transform: null,
		transformOrigin: null,
		u1: null,
		u2: null,
		underlinePosition: B,
		underlineThickness: B,
		unicode: null,
		unicodeBidi: null,
		unicodeRange: null,
		unitsPerEm: B,
		values: null,
		vAlphabetic: B,
		vMathematical: B,
		vectorEffect: null,
		vHanging: B,
		vIdeographic: B,
		version: null,
		vertAdvY: B,
		vertOriginX: B,
		vertOriginY: B,
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
		xHeight: B,
		y: null,
		y1: null,
		y2: null,
		yChannelSelector: null,
		z: null,
		zoomAndPan: null
	},
	space: "svg",
	transform: ka
}), Na = Da({
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
}), Pa = Da({
	attributes: { xmlnsxlink: "xmlns:xlink" },
	properties: {
		xmlnsXLink: null,
		xmlns: null
	},
	space: "xmlns",
	transform: Aa
}), Fa = Da({
	properties: {
		xmlBase: null,
		xmlLang: null,
		xmlSpace: null
	},
	space: "xml",
	transform(e, t) {
		return "xml:" + t.slice(3).toLowerCase();
	}
}), Ia = /[A-Z]/g, La = /-[a-z]/g, Ra = /^data[-\w.:]+$/i;
function za(e, t) {
	let n = ha(t), r = t, i = ga;
	if (n in e.normal) return e.property[e.normal[n]];
	if (n.length > 4 && n.slice(0, 4) === "data" && Ra.test(t)) {
		if (t.charAt(4) === "-") {
			let e = t.slice(5).replace(La, Va);
			r = "data" + e.charAt(0).toUpperCase() + e.slice(1);
		} else {
			let e = t.slice(4);
			if (!La.test(e)) {
				let n = e.replace(Ia, Ba);
				n.charAt(0) !== "-" && (n = "-" + n), t = "data" + n;
			}
		}
		i = Ta;
	}
	return new i(r, t);
}
function Ba(e) {
	return "-" + e.toLowerCase();
}
function Va(e) {
	return e.charAt(1).toUpperCase();
}
//#endregion
//#region node_modules/property-information/index.js
var Ha = ma([
	Oa,
	ja,
	Na,
	Pa,
	Fa
], "html"), Ua = ma([
	Oa,
	Ma,
	Na,
	Pa,
	Fa
], "svg");
//#endregion
//#region node_modules/comma-separated-tokens/index.js
function Wa(e) {
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
var Ga = /[#.]/g;
function Ka(e, t) {
	let n = e || "", r = {}, i = 0, a, o;
	for (; i < n.length;) {
		Ga.lastIndex = i;
		let e = Ga.exec(n), t = n.slice(i, e ? e.index : n.length);
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
function qa(e) {
	let t = String(e || "").trim();
	return t ? t.split(/[ \t\n\r\f]+/g) : [];
}
//#endregion
//#region node_modules/hastscript/lib/create-h.js
function Ja(e, t, n) {
	let r = n ? eo(n) : void 0;
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
			o = Ka(n, t);
			let s = o.tagName.toLowerCase(), c = r ? r.get(s) : void 0;
			if (o.tagName = c || s, Ya(i)) a.unshift(i);
			else for (let [t, n] of Object.entries(i)) Xa(e, o.properties, t, n);
		}
		for (let e of a) Za(o.children, e);
		return o.type === "element" && o.tagName === "template" && (o.content = {
			type: "root",
			children: o.children
		}, o.children = []), o;
	}
	return i;
}
function Ya(e) {
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
function Xa(e, t, n, r) {
	let i = za(e, n), a;
	if (r != null) {
		if (typeof r == "number") {
			if (Number.isNaN(r)) return;
			a = r;
		} else a = typeof r == "boolean" ? r : typeof r == "string" ? i.spaceSeparated ? qa(r) : i.commaSeparated ? Wa(r) : i.commaOrSpaceSeparated ? qa(Wa(r).join(" ")) : Qa(i, i.property, r) : Array.isArray(r) ? [...r] : i.property === "style" ? $a(r) : String(r);
		if (Array.isArray(a)) {
			let e = [];
			for (let t of a) e.push(Qa(i, i.property, t));
			a = e;
		}
		i.property === "className" && Array.isArray(t.className) && (a = t.className.concat(a)), t[i.property] = a;
	}
}
function Za(e, t) {
	if (t != null) {
		if (typeof t == "number" || typeof t == "string") e.push({
			type: "text",
			value: String(t)
		});
		else if (Array.isArray(t)) for (let n of t) Za(e, n);
		else if (typeof t == "object" && "type" in t) t.type === "root" ? Za(e, t.children) : e.push(t);
		else throw Error("Expected node, nodes, or string, got `" + t + "`");
	}
}
function Qa(e, t, n) {
	if (typeof n == "string") {
		if (e.number && n && !Number.isNaN(Number(n))) return Number(n);
		if ((e.boolean || e.overloadedBoolean) && (n === "" || ha(n) === ha(t))) return !0;
	}
	return n;
}
function $a(e) {
	let t = [];
	for (let [n, r] of Object.entries(e)) t.push([n, r].join(": "));
	return t.join("; ");
}
function eo(e) {
	let t = /* @__PURE__ */ new Map();
	for (let n of e) t.set(n.toLowerCase(), n);
	return t;
}
//#endregion
//#region node_modules/hastscript/lib/svg-case-sensitive-tag-names.js
var to = /* @__PURE__ */ "altGlyph.altGlyphDef.altGlyphItem.animateColor.animateMotion.animateTransform.clipPath.feBlend.feColorMatrix.feComponentTransfer.feComposite.feConvolveMatrix.feDiffuseLighting.feDisplacementMap.feDistantLight.feDropShadow.feFlood.feFuncA.feFuncB.feFuncG.feFuncR.feGaussianBlur.feImage.feMerge.feMergeNode.feMorphology.feOffset.fePointLight.feSpecularLighting.feSpotLight.feTile.feTurbulence.foreignObject.glyphRef.linearGradient.radialGradient.solidColor.textArea.textPath".split("."), no = Ja(Ha, "div");
Ja(Ua, "g", to);
//#endregion
//#region node_modules/character-entities-legacy/index.js
var ro = /* @__PURE__ */ "AElig.AMP.Aacute.Acirc.Agrave.Aring.Atilde.Auml.COPY.Ccedil.ETH.Eacute.Ecirc.Egrave.Euml.GT.Iacute.Icirc.Igrave.Iuml.LT.Ntilde.Oacute.Ocirc.Ograve.Oslash.Otilde.Ouml.QUOT.REG.THORN.Uacute.Ucirc.Ugrave.Uuml.Yacute.aacute.acirc.acute.aelig.agrave.amp.aring.atilde.auml.brvbar.ccedil.cedil.cent.copy.curren.deg.divide.eacute.ecirc.egrave.eth.euml.frac12.frac14.frac34.gt.iacute.icirc.iexcl.igrave.iquest.iuml.laquo.lt.macr.micro.middot.nbsp.not.ntilde.oacute.ocirc.ograve.ordf.ordm.oslash.otilde.ouml.para.plusmn.pound.quot.raquo.reg.sect.shy.sup1.sup2.sup3.szlig.thorn.times.uacute.ucirc.ugrave.uml.uuml.yacute.yen.yuml".split("."), io = {
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
function ao(e) {
	let t = typeof e == "string" ? e.charCodeAt(0) : e;
	return t >= 48 && t <= 57;
}
//#endregion
//#region node_modules/is-hexadecimal/index.js
function oo(e) {
	let t = typeof e == "string" ? e.charCodeAt(0) : e;
	return t >= 97 && t <= 102 || t >= 65 && t <= 70 || t >= 48 && t <= 57;
}
//#endregion
//#region node_modules/is-alphabetical/index.js
function so(e) {
	let t = typeof e == "string" ? e.charCodeAt(0) : e;
	return t >= 97 && t <= 122 || t >= 65 && t <= 90;
}
//#endregion
//#region node_modules/is-alphanumerical/index.js
function co(e) {
	return so(e) || ao(e);
}
//#endregion
//#region node_modules/decode-named-character-reference/index.dom.js
var lo = document.createElement("i");
function uo(e) {
	let t = "&" + e + ";";
	lo.innerHTML = t;
	let n = lo.textContent;
	return n.charCodeAt(n.length - 1) === 59 && e !== "semi" ? !1 : n !== t && n;
}
//#endregion
//#region node_modules/parse-entities/lib/index.js
var H = [
	"",
	"Named character references must be terminated by a semicolon",
	"Numeric character references must be terminated by a semicolon",
	"Named character references cannot be empty",
	"Numeric character references cannot be empty",
	"Named character references must be known",
	"Numeric character references cannot be disallowed",
	"Numeric character references cannot be outside the permissible Unicode range"
];
function U(e, t) {
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
		let _ = "", v = "", y = "", b = u === "named" ? co : u === "decimal" ? ao : oo;
		for (l--; ++l <= e.length;) {
			let t = e.charCodeAt(l);
			if (!b(t)) break;
			y += String.fromCharCode(t), u === "named" && ro.includes(y) && (_ = y, v = uo(y));
		}
		let x = e.charCodeAt(l) === 59;
		if (x) {
			l++;
			let e = u === "named" && uo(y);
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
						n === 61 ? (h(t, S), v = "") : co(n) ? v = "" : h(t, S);
					} else h(t, S);
				}
				C = v;
			} else {
				x || h(2, S);
				let e = Number.parseInt(y, u === "hexadecimal" ? 16 : 10);
				if (fo(e)) h(7, S), C = "�";
				else if (e in io) h(6, S), C = io[e];
				else {
					let t = "";
					po(e) && h(6, S), e > 65535 && (e -= 65536, t += String.fromCharCode(e >>> 10 | 55296), e = 56320 | e & 1023), C = t + String.fromCharCode(e);
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
		n.warning && (r = m(), r.column += t, r.offset += t, n.warning.call(n.warningContext || void 0, H[e], r, e));
	}
	function g() {
		s &&= (i.push(s), n.text && n.text.call(n.textContext || void 0, s, {
			start: f,
			end: m()
		}), "");
	}
}
function fo(e) {
	return e >= 55296 && e <= 57343 || e > 1114111;
}
function po(e) {
	return e >= 1 && e <= 8 || e === 11 || e >= 13 && e <= 31 || e >= 127 && e <= 159 || e >= 64976 && e <= 65007 || (e & 65535) == 65535 || (e & 65535) == 65534;
}
//#endregion
//#region node_modules/refractor/lib/prism-core.js
var mo = 0, ho = {}, go = {
	util: {
		type: function(e) {
			return Object.prototype.toString.call(e).slice(8, -1);
		},
		objId: function(e) {
			return e.__id || Object.defineProperty(e, "__id", { value: ++mo }), e.__id;
		},
		clone: function e(t, n) {
			n ||= {};
			var r, i;
			switch (go.util.type(t)) {
				case "Object":
					if (i = go.util.objId(t), n[i]) return n[i];
					for (var a in r = {}, n[i] = r, t) t.hasOwnProperty(a) && (r[a] = e(t[a], n));
					return r;
				case "Array": return i = go.util.objId(t), n[i] ? n[i] : (r = [], n[i] = r, t.forEach(function(t, i) {
					r[i] = e(t, n);
				}), r);
				default: return t;
			}
		}
	},
	languages: {
		plain: ho,
		plaintext: ho,
		text: ho,
		txt: ho,
		extend: function(e, t) {
			var n = go.util.clone(go.languages[e]);
			for (var r in t) n[r] = t[r];
			return n;
		},
		insertBefore: function(e, t, n, r) {
			r ||= go.languages;
			var i = r[e], a = {};
			for (var o in i) if (i.hasOwnProperty(o)) {
				if (o == t) for (var s in n) n.hasOwnProperty(s) && (a[s] = n[s]);
				n.hasOwnProperty(o) || (a[o] = i[o]);
			}
			var c = r[e];
			return r[e] = a, go.languages.DFS(go.languages, function(t, n) {
				n === c && t != e && (this[t] = a);
			}), a;
		},
		DFS: function e(t, n, r, i) {
			i ||= {};
			var a = go.util.objId;
			for (var o in t) if (t.hasOwnProperty(o)) {
				n.call(t, o, t[o], r || o);
				var s = t[o], c = go.util.type(s);
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
		if (go.hooks.run("before-tokenize", r), !r.grammar) throw Error("The language \"" + r.language + "\" has no grammar.");
		return r.tokens = go.tokenize(r.code, r.grammar), go.hooks.run("after-tokenize", r), _o.stringify(go.util.encode(r.tokens), r.language);
	},
	tokenize: function(e, t) {
		var n = t.rest;
		if (n) {
			for (var r in n) t[r] = n[r];
			delete t.rest;
		}
		var i = new bo();
		return xo(i, i.head, e), yo(e, i, t, i.head, 0), Co(i);
	},
	hooks: {
		all: {},
		add: function(e, t) {
			var n = go.hooks.all;
			n[e] = n[e] || [], n[e].push(t);
		},
		run: function(e, t) {
			var n = go.hooks.all[e];
			if (!(!n || !n.length)) for (var r = 0, i; i = n[r++];) i(t);
		}
	},
	Token: _o
};
function _o(e, t, n, r) {
	this.type = e, this.content = t, this.alias = n, this.length = (r || "").length | 0;
}
function vo(e, t, n, r) {
	e.lastIndex = t;
	var i = e.exec(n);
	if (i && r && i[1]) {
		var a = i[1].length;
		i.index += a, i[0] = i[0].slice(a);
	}
	return i;
}
function yo(e, t, n, r, i, a) {
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
				if (!(v instanceof _o)) {
					var y = 1, b;
					if (f) {
						if (b = vo(h, _, e, d), !b || b.index >= e.length) break;
						var x = b.index, S = b.index + b[0].length, C = _;
						for (C += g.value.length; x >= C;) g = g.next, C += g.value.length;
						if (C -= g.value.length, _ = C, g.value instanceof _o) continue;
						for (var w = g; w !== t.tail && (C < S || typeof w.value == "string"); w = w.next) y++, C += w.value.length;
						y--, v = e.slice(_, C), b.index -= _;
					} else if (b = vo(h, 0, v, d), !b) continue;
					var x = b.index, T = b[0], E = v.slice(0, x), ee = v.slice(x + T.length), D = _ + v.length;
					a && D > a.reach && (a.reach = D);
					var O = g.prev;
					E && (O = xo(t, O, E), _ += E.length), So(t, O, y);
					var te = new _o(o, u ? go.tokenize(T, u) : T, p, T);
					if (g = xo(t, O, te), ee && xo(t, g, ee), y > 1) {
						var k = {
							cause: o + "," + c,
							reach: D
						};
						yo(e, t, n, g.prev, _, k), a && k.reach > a.reach && (a.reach = k.reach);
					}
				}
			}
		}
	}
}
function bo() {
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
function xo(e, t, n) {
	var r = t.next, i = {
		value: n,
		prev: t,
		next: r
	};
	return t.next = i, r.prev = i, e.length++, i;
}
function So(e, t, n) {
	for (var r = t.next, i = 0; i < n && r !== e.tail; i++) r = r.next;
	t.next = r, r.prev = t, e.length -= i;
}
function Co(e) {
	for (var t = [], n = e.head.next; n !== e.tail;) t.push(n.value), n = n.next;
	return t;
}
var wo = go;
//#endregion
//#region node_modules/refractor/lib/core.js
function To() {}
To.prototype = wo;
var Eo = new To();
Eo.highlight = Do, Eo.register = Oo, Eo.alias = ko, Eo.registered = Ao, Eo.listLanguages = jo, Eo.util.encode = No, Eo.Token.stringify = Mo;
function Do(e, t) {
	if (typeof e != "string") throw TypeError("Expected `string` for `value`, got `" + e + "`");
	let n, r;
	/* c8 ignore next 2 */
	if (t && typeof t == "object") n = t;
	else {
		if (r = t, typeof r != "string") throw TypeError("Expected `string` for `name`, got `" + r + "`");
		if (Object.hasOwn(Eo.languages, r)) n = Eo.languages[r];
		else throw Error("Unknown language: `" + r + "` is not registered");
	}
	return {
		type: "root",
		children: wo.highlight.call(Eo, e, n, r)
	};
}
function Oo(e) {
	if (typeof e != "function" || !e.displayName) throw Error("Expected `function` for `syntax`, got `" + e + "`");
	Object.hasOwn(Eo.languages, e.displayName) || e(Eo);
}
function ko(e, t) {
	let n = Eo.languages, r = {};
	typeof e == "string" ? t && (r[e] = t) : r = e;
	let i;
	for (i in r) if (Object.hasOwn(r, i)) {
		let e = r[i], t = typeof e == "string" ? [e] : e, a = -1;
		for (; ++a < t.length;) n[t[a]] = n[i];
	}
}
function Ao(e) {
	if (typeof e != "string") throw TypeError("Expected `string` for `aliasOrLanguage`, got `" + e + "`");
	return Object.hasOwn(Eo.languages, e);
}
function jo() {
	let e = Eo.languages, t = [], n;
	for (n in e) Object.hasOwn(e, n) && typeof e[n] == "object" && t.push(n);
	return t;
}
function Mo(e, t) {
	if (typeof e == "string") return {
		type: "text",
		value: e
	};
	if (Array.isArray(e)) {
		let n = [], r = -1;
		for (; ++r < e.length;) e[r] !== null && e[r] !== void 0 && e[r] !== "" && n.push(Mo(e[r], t));
		return n;
	}
	let n = {
		attributes: {},
		classes: ["token", e.type],
		content: Mo(e.content, t),
		language: t,
		tag: "span",
		type: e.type
	};
	return e.alias && n.classes.push(...typeof e.alias == "string" ? [e.alias] : e.alias), Eo.hooks.run("wrap", n), no(n.tag + "." + n.classes.join("."), Po(n.attributes), n.content);
}
function No(e) {
	return e;
}
function Po(e) {
	let t;
	for (t in e) Object.hasOwn(e, t) && (e[t] = U(e[t]));
	return e;
}
Fo.displayName = "clike", Fo.aliases = [];
function Fo(e) {
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
Io.displayName = "csharp", Io.aliases = ["cs", "dotnet"];
function Io(e) {
	e.register(Fo), (function(e) {
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
		var ee = ":[^}\\r\\n]+", D = r(t("[^\"'/()]|<<0>>|\\(<<self>>*\\)", [C]), 2), O = t("\\{(?!\\{)(?:(?![}:])<<0>>)*<<1>>?\\}", [D, ee]), te = r(t("[^\"'/()]|\\/(?!\\*)|\\/\\*(?:[^*]|\\*(?!\\/))*\\*\\/|<<0>>|\\(<<self>>*\\)", [S]), 2), k = t("\\{(?!\\{)(?:(?![}:])<<0>>)*<<1>>?\\}", [te, ee]);
		function A(t, r) {
			return {
				interpolation: {
					pattern: n("((?:^|[^{])(?:\\{\\{)*)<<0>>", [t]),
					lookbehind: !0,
					inside: {
						"format-string": {
							pattern: n("(^\\{(?:(?![}:])<<0>>)*)<<1>>(?=\\}$)", [r, ee]),
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
				pattern: n("(^|[^\\\\])(?:\\$@|@\\$)\"(?:\"\"|\\\\[\\s\\S]|\\{\\{|<<0>>|[^\\\\{\"])*\"", [O]),
				lookbehind: !0,
				greedy: !0,
				inside: A(O, D)
			}, {
				pattern: n("(^|[^@\\\\])\\$\"(?:\\\\.|\\{\\{|<<0>>|[^\\\\\"{])*\"", [k]),
				lookbehind: !0,
				greedy: !0,
				inside: A(k, te)
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
var Lo = () => ({
	baseById: /* @__PURE__ */ new Map(),
	headById: /* @__PURE__ */ new Map(),
	baseByLine: /* @__PURE__ */ new Map(),
	headByLine: /* @__PURE__ */ new Map()
});
function Ro(e, t) {
	return e.nearestDepth === t.nearestDepth && e.viaDispatchOnly === t.viaDispatchOnly && e.looped === t.looped;
}
function zo(e, t, n) {
	if (!e || e.kind === "same") return "same";
	if (e.kind === "added") return t === "new" ? "added" : "same";
	if (e.kind === "removed") return t === "old" ? "removed" : "same";
	let r = t === "old" ? e.base : e.head;
	return r && Ro(r, n) ? "changed" : "same";
}
function Bo(e, t) {
	let n = new Map(e.map((e) => [e.family, e])), r = new Map(t.map((e) => [e.family, e])), i = [.../* @__PURE__ */ new Set([...n.keys(), ...r.keys()])].sort();
	return new Map(i.map((e) => {
		let t = n.get(e), i = r.get(e);
		return [e, {
			kind: t ? i ? Ro(t, i) ? "same" : "changed" : "removed" : "added",
			base: t,
			head: i
		}];
	}));
}
function Vo(e) {
	if (!e.id || e.name === "" || e.name.startsWith(".")) return null;
	let t = e.id.indexOf("("), n = t < 0 ? e.id : e.id.slice(0, t), r = n.lastIndexOf(".");
	if (r < 0) return null;
	let i = n.slice(0, r), a = t < 0 ? "" : e.id.slice(t);
	return `${i}|${e.signature ? e.signature.replace(e.name, "<method>") : ""}|${a}`;
}
function Ho(e, t, n) {
	let r = e.get(t) || [];
	r.push(n), e.set(t, r);
}
function Uo(e, t, n) {
	if (!n) return Lo();
	let r = new Map(e.map((e) => [e.id, e])), i = new Map(t.map((e) => [e.id, e])), a = [];
	for (let t of e) {
		let e = i.get(t.id);
		e && (a.push([t, e]), r.delete(t.id), i.delete(e.id));
	}
	let o = (e) => {
		let t = /* @__PURE__ */ new Map();
		for (let n of e) {
			let e = Vo(n);
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
		let t = Vo(e);
		t && u.has(t) || a.push([e, void 0]);
	}
	for (let e of i.values()) {
		let t = Vo(e);
		t && l.has(t) || a.push([void 0, e]);
	}
	let d = /* @__PURE__ */ new Map(), f = /* @__PURE__ */ new Map(), p = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map();
	for (let [e, t] of a) {
		let n = {
			base: e,
			head: t,
			effects: Bo(e?.effects || [], t?.effects || [])
		};
		e && (d.set(e.id, n), Ho(p, e.line, n)), t && (f.set(t.id, n), Ho(m, t.line, n));
	}
	return {
		baseById: d,
		headById: f,
		baseByLine: p,
		headByLine: m
	};
}
function Wo(e) {
	return [...e.effects.values()].filter((e) => e.kind !== "same");
}
//#endregion
//#region src/review-gutter.ts
function Go(e, t, n, r = !1) {
	return e === "split" ? t === "normal" && r && n === "old" ? null : n : n === "old" ? null : t === "delete" ? "old" : "new";
}
//#endregion
//#region src/review-presentation.ts
var Ko = [
	"inline",
	"gutter",
	"off"
], qo = "rig.review.effectMode", Jo = () => window.localStorage;
function Yo(e = Jo) {
	try {
		let t = e().getItem(qo);
		return Ko.includes(t) ? t : "inline";
	} catch {
		return "inline";
	}
}
function Xo(e, t = Jo) {
	try {
		t().setItem(qo, e);
	} catch {}
}
var Zo = {
	db: "Database",
	cache: "Cache",
	blob: "Object store",
	bus: "Message bus",
	echo: "Event channel",
	io: "File system / I/O",
	rpc: "Remote call",
	search: "Search"
};
function Qo(e) {
	return Zo[e] || e;
}
function $o(e) {
	return [
		Qo(e.family),
		e.nearestDepth === 0 ? "direct" : `depth ${e.nearestDepth}`,
		e.viaDispatchOnly ? "possible dispatch" : "",
		e.looped ? "inside iteration" : ""
	].filter(Boolean).join(" · ");
}
function es(e) {
	return `${e.provider}:${e.operation} · inside iteration`;
}
function ts(e) {
	return `${e.witnessProvider}:${e.witnessOperation} · reached from iterating call · depth ${e.witnessDepth} · candidate`;
}
function ns(e) {
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
function rs(e, t) {
	return `Show effects for ${e === "old" ? "base" : "head"} line ${t}`;
}
function is(e, t) {
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
function as(e, t, n) {
	return e && t === 0 && n === 0;
}
//#endregion
//#region src/file-diff.tsx
var os = [
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
], ss = /* @__PURE__ */ new WeakMap();
Eo.register(Io);
var cs = { highlight(e, t) {
	return Eo.highlight(e, t).children;
} };
function ls(e) {
	return e.slice(0, 12);
}
function us(e) {
	let t = e.replaceAll("\\", "/"), n = t.lastIndexOf("/");
	return {
		name: n < 0 ? t : t.slice(n + 1),
		parent: n < 0 ? "" : t.slice(0, n)
	};
}
function ds(e) {
	if (!e) return "external effect";
	let t = e.replace(/^[A-Z]:/, "").split("(", 1)[0];
	return (t.split(/[.:+]/).pop() || t).replace(/``\d+$/, "<T>");
}
function fs(e) {
	let t = e.nearestDepth === 0 ? "!" : `:${e.nearestDepth}`;
	return `${e.family}${t}${e.looped ? "*" : ""}${e.viaDispatchOnly ? "?" : ""}`;
}
function ps(e) {
	return [
		`${fs(e)} — ${e.nearestDepth === 0 ? "the effect is in this call's body" : `nearest is ${e.nearestDepth} calls below`}`,
		e.viaDispatchOnly ? "BASIS: virtual/interface dispatch only — a lead, not a proven call" : "BASIS: a real call edge",
		e.looped ? "ITERATION: an effectful edge occurs inside an iteration; runtime count is not established" : ""
	].filter(Boolean).join("\n");
}
function ms(e, t) {
	return t === "old" ? e.type === "insert" ? null : e.type === "delete" ? e.lineNumber : e.oldLineNumber : e.type === "delete" ? null : e.type === "insert" ? e.lineNumber : e.newLineNumber;
}
function hs(e, t) {
	let n = e.find((e) => e.family === t.family);
	if (!n) {
		e.push({ ...t });
		return;
	}
	let r = n.viaDispatchOnly && !t.viaDispatchOnly, i = n.viaDispatchOnly === t.viaDispatchOnly && t.nearestDepth < n.nearestDepth, a = n.looped || t.looped;
	r || i ? Object.assign(n, t, { looped: a }) : n.looped = a;
}
function gs(e) {
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
		for (let n of t.effects) hs(e.effects, n);
	}
	for (let t of e.findings?.hazards || []) n(t.line).hazards.push(t);
	for (let t of e.findings?.amplifications || []) n(t.line).amplifications.push(t);
	for (let t of e.findings?.anchors || []) n(t.line).anchors.push(t);
	for (let e of t.values()) e.effects.sort((e, t) => Number(e.viaDispatchOnly) - Number(t.viaDispatchOnly) || e.nearestDepth - t.nearestDepth || e.family.localeCompare(t.family));
	return t;
}
function _s(e, t) {
	return is(e, t);
}
function vs(e, t) {
	return e === "changed" ? "changed" : t === "old" && e === "removed" ? "removed" : t === "new" && e === "added" ? "added" : "same";
}
function ys(e, t, n, r, i) {
	let a = n.map((t) => vs(t.effects.get(e.family)?.kind, r)), o = (t?.sites || []).map((e) => (r === "old" ? i.baseById : i.headById).get(e.enclosingMethodId)).filter((e) => !!e).map((t) => zo(t.effects.get(e.family), r, e)), s = [...a, ...o];
	return s.includes("changed") ? "changed" : s.includes("added") ? "added" : s.includes("removed") ? "removed" : "same";
}
function bs(e, t) {
	let n = e.flatMap((e) => Wo(e).map((e) => {
		let n = Qo((t === "old" ? e.base : e.head)?.family || e.base?.family || e.head?.family || "effect");
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
function xs({ insight: e, headers: t, side: n, deltas: r }) {
	let i = [];
	for (let t of e?.effects || []) hs(i, t);
	for (let e of t) {
		let t = n === "old" ? e.base : e.head;
		for (let e of t?.effects || []) hs(i, e);
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
			children: os.map((i) => {
				let o = a.get(i.key), s = o ? ys(o, e, t, n, r) : "same", c = o ? `${i.label}: ${ps(o)}${s === "same" ? "" : `\nDELTA: ${s} at method grain`}` : i.label;
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
function Ss({ expanded: e, insight: t, headers: n = [], callbacks: r, deltas: i }) {
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
					bs([t], e.side)
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
						children: [es(e), /* @__PURE__ */ (0, m.jsxs)("span", {
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
						children: [ts(e), /* @__PURE__ */ (0, m.jsxs)("span", {
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
						/* @__PURE__ */ (0, m.jsx)("span", { children: ds(t.targetMethodId) }),
						t.effects.map((n, r) => {
							let a = zo((e.side === "old" ? i.baseById : i.headById).get(t.enclosingMethodId)?.effects.get(n.family), e.side, n);
							return /* @__PURE__ */ (0, m.jsxs)("span", {
								className: a === "same" ? "rig-diff-effect-detail" : `rig-diff-effect-detail rig-diff-inline-delta ${a}`,
								title: `${ps(n)}${a === "same" ? "" : `\n${a} at method grain`}`,
								children: [a === "same" ? "" : a === "added" ? "+ " : a === "removed" ? "− " : "△ ", $o(n)]
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
function Cs({ model: e, callbacks: t }) {
	let n = (0, h.useRef)(null), [r, i] = (0, h.useState)("unified"), [a, o] = (0, h.useState)(!0), [s, c] = (0, h.useState)(() => Yo()), [l, u] = (0, h.useState)(null), d = (0, h.useMemo)(() => ({}), [
		e.file,
		e.base.store,
		e.head.store,
		e.base.commit,
		e.head.commit,
		e.patch
	]), f = l?.context === d ? l : null, [p, g] = (0, h.useState)(null), _ = (0, h.useMemo)(() => e.patch.trim() ? ce(e.patch) : [], [e.patch]), v = (0, h.useMemo)(() => gs(e.base), [e.base]), y = (0, h.useMemo)(() => gs(e.head), [e.head]), b = _[0], x = (0, h.useMemo)(() => us(e.relativePath || e.file), [e.relativePath, e.file]), S = (0, h.useMemo)(() => b ? b.hunks.reduce((e, t) => {
		for (let n of t.changes) n.type === "insert" ? e.additions += 1 : n.type === "delete" && (e.deletions += 1);
		return e;
	}, {
		additions: 0,
		deletions: 0
	}) : {
		additions: 0,
		deletions: 0
	}, [b]), C = e.base.semanticState === "available" && e.base.effects !== null, w = e.head.semanticState === "available" && e.head.effects !== null, T = (0, h.useMemo)(() => Uo(e.base.effects?.methods || [], e.head.effects?.methods || [], C && w), [
		e.base.effects,
		e.head.effects,
		C,
		w
	]), E = e.base.effects?.sites.length || 0, ee = e.head.effects?.sites.length || 0, D = ns(e.base), O = ns(e.head), te = ee - E, k = C && w ? `effect sites ${te > 0 ? "+" : ""}${te}` : C ? "base-only semantics" : w ? "head-only semantics" : e.language === "text" ? "text-only · semantics unavailable" : "semantics unavailable", A = (0, h.useMemo)(() => b && e.language === "csharp" ? fa(b.hunks, {
		highlight: !0,
		refractor: cs,
		language: "csharp",
		enhancers: [ua(b.hunks)]
	}) : null, [b, e.language]), ne = (0, h.useMemo)(() => {
		let e = /* @__PURE__ */ new Map(), t = (e) => new Map([...e].map(([e, t]) => [e, t.filter((e) => Wo(e).length > 0)])), n = t(T.baseByLine), r = t(T.headByLine);
		for (let t of b?.hunks || []) for (let i of t.changes) {
			let t = (e) => {
				let t = ms(i, e);
				if (t == null) return;
				let a = (e === "old" ? v : y).get(t), o = (e === "old" ? n : r).get(t) || [];
				return a || o.length ? {
					side: e,
					line: t,
					insight: a,
					headers: o
				} : void 0;
			}, a = t("old"), o = t("new"), s = _s(a?.insight, o?.insight) && (a?.headers.length || 0) === (o?.headers.length || 0) && (a?.headers || []).every((e, t) => e === o?.headers[t]);
			e.set(Or(i), {
				change: i,
				old: a,
				new: o,
				identical: s
			});
		}
		return e;
	}, [
		b,
		v,
		y,
		T
	]), re = (e) => u((t) => t?.context === d && t.key === e.key && t.side === e.side ? null : {
		...e,
		context: d
	}), ie = (0, h.useMemo)(() => {
		let e = {};
		if (s === "off" || !f) return e;
		let n = ne.get(f.key), i = n?.[f.side];
		if (!n || !i) return e;
		let a = /* @__PURE__ */ (0, m.jsx)(Ss, {
			expanded: f,
			insight: i.insight,
			headers: i.headers,
			callbacks: t,
			deltas: T
		});
		return e[f.key] = r === "split" && n.change.type === "normal" ? /* @__PURE__ */ (0, m.jsxs)("div", {
			className: "rig-diff-inline-pair",
			children: [/* @__PURE__ */ (0, m.jsx)("div", { children: f.side === "old" ? a : null }), /* @__PURE__ */ (0, m.jsx)("div", { children: f.side === "new" ? a : null })]
		}) : /* @__PURE__ */ (0, m.jsx)("div", {
			className: "rig-diff-inline-single",
			children: a
		}), e;
	}, [
		ne,
		s,
		f,
		r,
		t,
		d,
		T
	]);
	return (0, h.useEffect)(() => u(null), [d]), (0, h.useEffect)(() => {
		let e = t.focusLine;
		if (!e) {
			g(null);
			return;
		}
		let r = requestAnimationFrame(() => {
			let t = n.current?.querySelector(`.rig-diff-gutter[data-rig-side="${e.side}"][data-rig-line="${e.line}"]`);
			g(!!t), t?.closest("tr")?.scrollIntoView({ block: "center" });
		});
		return () => cancelAnimationFrame(r);
	}, [
		t.focusLine?.line,
		t.focusLine?.side,
		e.patch,
		r
	]), /* @__PURE__ */ (0, m.jsxs)("div", {
		className: `rig-diff-island view-${r} effects-${s} ${a ? "wrap-lines" : "no-wrap"}`,
		ref: n,
		children: [
			/* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-head",
				children: [/* @__PURE__ */ (0, m.jsxs)("div", {
					className: "rig-diff-identity",
					title: e.relativePath,
					children: [
						/* @__PURE__ */ (0, m.jsxs)("div", {
							className: "rig-diff-file-line",
							children: [
								/* @__PURE__ */ (0, m.jsx)("span", {
									className: `rig-diff-status status-${e.status.toLowerCase()}`,
									title: `Git status ${e.status}`,
									children: e.status
								}),
								/* @__PURE__ */ (0, m.jsx)("strong", { children: x.name }),
								/* @__PURE__ */ (0, m.jsxs)("span", {
									className: "rig-diff-patch-counts",
									"aria-label": `${S.additions} additions, ${S.deletions} deletions`,
									children: [/* @__PURE__ */ (0, m.jsxs)("b", { children: ["+", S.additions] }), /* @__PURE__ */ (0, m.jsxs)("i", { children: ["−", S.deletions] })]
								})
							]
						}),
						x.parent ? /* @__PURE__ */ (0, m.jsx)("span", {
							className: "rig-diff-parent",
							children: x.parent
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
								ls(e.base.commit),
								" → ",
								ls(e.head.commit)
							]
						})
					]
				}), /* @__PURE__ */ (0, m.jsxs)("div", {
					className: "rig-diff-summary",
					children: [
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
							className: `rig-diff-effect-delta ${te > 0 ? "added" : te < 0 ? "removed" : "stable"}`,
							title: C && w ? `${E} base effect sites → ${ee} head effect sites` : `base: ${e.base.semanticState}; head: ${e.head.semanticState}`,
							children: k
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
									/* @__PURE__ */ (0, m.jsxs)("fieldset", { children: [/* @__PURE__ */ (0, m.jsx)("legend", { children: "Effect annotations" }), Ko.map((e) => /* @__PURE__ */ (0, m.jsxs)("label", { children: [/* @__PURE__ */ (0, m.jsx)("input", {
										type: "radio",
										name: "rig-effect-display",
										value: e,
										checked: s === e,
										onChange: () => {
											c(e), Xo(e), u(null);
										}
									}), e === "inline" ? "Inline" : e === "gutter" ? "Gutter" : "Off"] }, e))] }),
									/* @__PURE__ */ (0, m.jsxs)("fieldset", { children: [
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
									] }),
									/* @__PURE__ */ (0, m.jsxs)("label", {
										className: "rig-diff-settings-check",
										children: [/* @__PURE__ */ (0, m.jsx)("input", {
											type: "checkbox",
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
						"data-state": D.state,
						title: D.detail,
						children: ["Base: ", D.label]
					}),
					/* @__PURE__ */ (0, m.jsxs)("span", {
						"data-findings-side": "head",
						"data-state": O.state,
						title: O.detail,
						children: ["Head: ", O.label]
					})
				]
			}),
			s === "gutter" && (C || w) ? /* @__PURE__ */ (0, m.jsxs)("div", {
				className: "rig-diff-lane-key",
				title: "Effect reach by family",
				children: [/* @__PURE__ */ (0, m.jsx)("span", {
					className: "rig-diff-lanehead",
					"aria-label": "Effect lane columns",
					children: os.map((e) => /* @__PURE__ */ (0, m.jsx)("b", {
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
			t.focusLine && p === !1 ? /* @__PURE__ */ (0, m.jsxs)("div", {
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
			b ? /* @__PURE__ */ (0, m.jsx)(mi, {
				viewType: r,
				diffType: b.type,
				hunks: b.hunks,
				tokens: A,
				widgets: ie,
				renderGutter: ({ change: e, side: n, renderDefault: i, wrapInAnchor: a }) => {
					let o = ms(e, n), c = Or(e), l = ne.get(c), u = as(l?.identical || !1, l?.old?.headers.length || 0, l?.new?.headers.length || 0), d = s === "off" ? null : Go(r, e.type, n, u), p = d == null ? null : ms(e, d), h = d == null ? void 0 : l?.[d], g = h?.insight, _ = h?.headers || [], v = t.focusLine?.side === n && t.focusLine.line === o, y = _.length > 0 || (g?.effects || []).some((e) => ys(e, g, _, d, T) !== "same"), b = !!g?.hazards.length, x = !!(g?.amplifications.length || g?.anchors.length), S = s === "gutter" && (g || _.length) ? /* @__PURE__ */ (0, m.jsx)(xs, {
						insight: g,
						headers: _,
						side: d,
						deltas: T
					}) : null, C = [
						d == null ? "" : bs(_, d),
						...(g?.effects || []).map($o),
						b ? "Hazard findings — click for details" : "",
						x ? "Iteration candidate — not proof of runtime N+1" : ""
					].filter(Boolean).join("\n");
					return a(/* @__PURE__ */ (0, m.jsxs)("span", {
						className: `rig-diff-gutter${v ? " focus" : ""}${_.length ? " method-change" : ""}`,
						"data-rig-side": n,
						"data-rig-line": o ?? void 0,
						title: d == null ? void 0 : bs(_, d) || void 0,
						children: [g || _.length ? /* @__PURE__ */ (0, m.jsx)("button", {
							type: "button",
							className: s === "inline" ? `rig-diff-disclosure-trigger${y ? " changed" : ""}${b ? " hazard" : ""}${x ? " amplification" : ""}` : "rig-diff-mark-button",
							title: C || "Show effects and open their call trees",
							"aria-label": rs(d, p),
							"aria-expanded": f?.key === c && f.side === d,
							"data-rig-side": d ?? void 0,
							"data-rig-line": p ?? void 0,
							onClick: (e) => {
								e.preventDefault(), e.stopPropagation(), re({
									key: c,
									side: d,
									line: p
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
							}) : S
						}) : S, i()]
					}));
				},
				children: (e) => e.map((e) => /* @__PURE__ */ (0, m.jsx)(li, { hunk: e }, e.content))
			}) : /* @__PURE__ */ (0, m.jsx)("div", {
				className: "rig-diff-empty",
				children: "No textual changes in this file."
			})
		]
	});
}
function ws(e, t, n = {}) {
	let r = ss.get(e);
	r || (r = (0, p.createRoot)(e), ss.set(e, r)), r.render(/* @__PURE__ */ (0, m.jsx)(Cs, {
		model: t,
		callbacks: n
	}));
}
function Ts(e) {
	ss.get(e)?.unmount(), ss.delete(e);
}
//#endregion
export { ws as mountFileDiff, Ts as unmountFileDiff };
