// Spatail runtime library.
//
// This file is loaded into a fresh JavaScript engine before every generated program. It turns the handful of
// native functions the host exposes (`__host`) into the API programs are written against: `scene`, `space`,
// `user`, `voice`, `kit`, `vec`, `quat`, `theme`, `state`, `log`.
//
// The same file runs on the headset and phone (inside Jint) and in the browser lab (natively), so a program
// behaves the same everywhere. Conventions: metres, Y up, +Z forward, left-handed (Unity's). Vectors are
// [x, y, z] arrays, rotations are [x, y, z, w] quaternions; wherever a rotation is accepted, three numbers are
// read as Euler degrees.
(function (global) {
  'use strict';

  var H = global.__host;
  var DEG = Math.PI / 180;

  // ---- maths -------------------------------------------------------------------------------

  var vec = {
    add: function (a, b) { return [a[0] + b[0], a[1] + b[1], a[2] + b[2]]; },
    sub: function (a, b) { return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]; },
    scale: function (a, s) { return [a[0] * s, a[1] * s, a[2] * s]; },
    mul: function (a, b) { return [a[0] * b[0], a[1] * b[1], a[2] * b[2]]; },
    dot: function (a, b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; },
    cross: function (a, b) { return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]; },
    length: function (a) { return Math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]); },
    distance: function (a, b) { return vec.length(vec.sub(a, b)); },
    normalize: function (a) { var l = vec.length(a); return l > 1e-9 ? [a[0] / l, a[1] / l, a[2] / l] : [0, 0, 0]; },
    lerp: function (a, b, t) { return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]; },
    flat: function (a) { return [a[0], 0, a[2]]; },
    up: [0, 1, 0], right: [1, 0, 0], forward: [0, 0, 1], zero: [0, 0, 0]
  };

  var quat = {
    identity: [0, 0, 0, 1],
    axisAngle: function (axis, degrees) {
      var a = vec.normalize(axis), h = degrees * DEG * 0.5, s = Math.sin(h);
      return [a[0] * s, a[1] * s, a[2] * s, Math.cos(h)];
    },
    mul: function (a, b) {
      return [
        a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
        a[3] * b[1] + a[1] * b[3] + a[2] * b[0] - a[0] * b[2],
        a[3] * b[2] + a[2] * b[3] + a[0] * b[1] - a[1] * b[0],
        a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]];
    },
    // Euler degrees in Unity's order: Z, then X, then Y.
    euler: function (x, y, z) {
      if (x && x.length === 3) { z = x[2]; y = x[1]; x = x[0]; }
      return quat.mul(quat.mul(quat.axisAngle([0, 1, 0], y || 0), quat.axisAngle([1, 0, 0], x || 0)), quat.axisAngle([0, 0, 1], z || 0));
    },
    rotate: function (q, v) {
      var x2 = q[0] * 2, y2 = q[1] * 2, z2 = q[2] * 2;
      var xx = q[0] * x2, yy = q[1] * y2, zz = q[2] * z2, xy = q[0] * y2, xz = q[0] * z2, yz = q[1] * z2;
      var wx = q[3] * x2, wy = q[3] * y2, wz = q[3] * z2;
      return [
        (1 - (yy + zz)) * v[0] + (xy - wz) * v[1] + (xz + wy) * v[2],
        (xy + wz) * v[0] + (1 - (xx + zz)) * v[1] + (yz - wx) * v[2],
        (xz - wy) * v[0] + (yz + wx) * v[1] + (1 - (xx + yy)) * v[2]];
    },
    inverse: function (q) { return [-q[0], -q[1], -q[2], q[3]]; },
    // Rotation whose +Z looks along `forward`, keeping +Y as close to `up` as it can.
    look: function (forward, up) {
      var f = vec.normalize(forward);
      if (vec.length(f) < 1e-6) return [0, 0, 0, 1];
      var r = vec.cross(up || [0, 1, 0], f);
      if (vec.length(r) < 1e-6) r = vec.cross(Math.abs(f[1]) > 0.9 ? [0, 0, 1] : [0, 1, 0], f);
      r = vec.normalize(r);
      var u = vec.cross(f, r);
      var m00 = r[0], m01 = u[0], m02 = f[0], m10 = r[1], m11 = u[1], m12 = f[1], m20 = r[2], m21 = u[2], m22 = f[2];
      var tr = m00 + m11 + m22, s;
      if (tr > 0) { s = Math.sqrt(tr + 1) * 2; return [(m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25 * s]; }
      if (m00 > m11 && m00 > m22) { s = Math.sqrt(1 + m00 - m11 - m22) * 2; return [0.25 * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s]; }
      if (m11 > m22) { s = Math.sqrt(1 + m11 - m00 - m22) * 2; return [(m01 + m10) / s, 0.25 * s, (m12 + m21) / s, (m02 - m20) / s]; }
      s = Math.sqrt(1 + m22 - m00 - m11) * 2; return [(m02 + m20) / s, (m12 + m21) / s, 0.25 * s, (m10 - m01) / s];
    },
    // Shortest rotation taking direction `a` to direction `b`.
    fromTo: function (a, b) {
      a = vec.normalize(a); b = vec.normalize(b);
      var d = vec.dot(a, b);
      if (d > 0.99999) return [0, 0, 0, 1];
      if (d < -0.99999) {
        var axis = vec.cross([1, 0, 0], a);
        if (vec.length(axis) < 1e-3) axis = vec.cross([0, 1, 0], a);
        return quat.axisAngle(axis, 180);
      }
      var c = vec.cross(a, b), q = [c[0], c[1], c[2], 1 + d];
      var l = Math.sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
      return [q[0] / l, q[1] / l, q[2] / l, q[3] / l];
    },
    slerp: function (a, b, t) {
      var d = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
      if (d < 0) { b = [-b[0], -b[1], -b[2], -b[3]]; d = -d; }
      if (d > 0.9995) {
        var n = [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, a[3] + (b[3] - a[3]) * t];
        var nl = Math.sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2] + n[3] * n[3]);
        return [n[0] / nl, n[1] / nl, n[2] / nl, n[3] / nl];
      }
      var th = Math.acos(d), sa = Math.sin((1 - t) * th) / Math.sin(th), sb = Math.sin(t * th) / Math.sin(th);
      return [a[0] * sa + b[0] * sb, a[1] * sa + b[1] * sb, a[2] * sa + b[2] * sb, a[3] * sa + b[3] * sb];
    }
  };

  function toQuat(r) {
    if (!r) return [0, 0, 0, 1];
    return r.length === 3 ? quat.euler(r[0], r[1], r[2]) : [r[0], r[1], r[2], r[3]];
  }
  function toScale(s) {
    if (s === undefined || s === null) return [1, 1, 1];
    return typeof s === 'number' ? [s, s, s] : [s[0], s[1], s[2]];
  }
  function point(p) {
    // Accepts a vector, or anything that carries a position (a hit, a prop, a node).
    if (!p) return [0, 0, 0];
    if (typeof p.worldPosition === 'function') return p.worldPosition();
    if (p.position) return [p.position[0], p.position[1], p.position[2]];
    return [p[0], p[1], p[2]];
  }

  // ---- theme -------------------------------------------------------------------------------

  var theme = {
    accent: '#6C5CE7', accentDeep: '#4B3FC4', accentSoft: '#E9E6FB',
    warning: '#E8722C', warningSoft: '#FDEBDD', success: '#2BA471',
    ink: '#17161D', muted: '#7B7A8C', surface: '#FFFFFF', surfaceDim: '#F4F3FA',
    // Text sizes are em heights in metres, tuned for reading at 1 to 3 m.
    text: { small: 0.018, body: 0.022, label: 0.028, title: 0.03, large: 0.045 },
    layer: 0.0025
  };
  function color(c) {
    if (!c) return theme.accent;
    if (c.charAt(0) === '#') return c;
    return theme[c] || theme.accent;
  }

  // ---- scheduling --------------------------------------------------------------------------

  var frameCallbacks = [], timers = [], tweens = [], clock = 0;
  var eases = {
    linear: function (t) { return t; },
    in: function (t) { return t * t * t; },
    out: function (t) { return 1 - Math.pow(1 - t, 3); },
    inOut: function (t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; },
    back: function (t) { return 1 + 2.2 * Math.pow(t - 1, 3) + 1.2 * Math.pow(t - 1, 2); }
  };

  // An error as text a person (or the planner, when repairing) can act on: the message first, then where.
  function describe(e) {
    if (e && e.message !== undefined) return (e.name || 'Error') + ': ' + e.message + (e.stack ? '\n' + e.stack : '');
    return String(e);
  }
  global.__describe = describe;

  function guard(fn, what) {
    try { return fn(); }
    catch (e) { H.log('error', what + ': ' + describe(e)); }
  }

  function remover(list, item) {
    return function () { var i = list.indexOf(item); if (i >= 0) list.splice(i, 1); };
  }

  // Runs `step(t)` with t going 0..1 over `duration` seconds. Returns a promise that resolves when it finishes.
  function tween(duration, step, options) {
    options = options || {};
    var ease = eases[options.ease || 'out'] || eases.out;
    return new Promise(function (resolve) {
      var item = { t: -(options.delay || 0), d: Math.max(duration, 1e-4), step: step, ease: ease, loop: !!options.loop, done: resolve };
      tweens.push(item);
      if (item.t >= 0) guard(function () { step(ease(0)); }, 'tween');
    });
  }

  global.__tick = function (dt, time) {
    clock = time;
    var i;
    for (i = timers.length - 1; i >= 0; i--) {
      var timer = timers[i];
      timer.left -= dt;
      if (timer.left > 0) continue;
      if (timer.every > 0) timer.left += timer.every; else timers.splice(i, 1);
      guard(timer.fn, 'timer');
    }
    for (i = tweens.length - 1; i >= 0; i--) {
      var tw = tweens[i];
      tw.t += dt;
      if (tw.t < 0) continue;
      var k = Math.min(1, tw.t / tw.d);
      guard(function () { tw.step(tw.ease(k)); }, 'tween');
      if (k >= 1) {
        if (tw.loop) tw.t = 0; else { tweens.splice(i, 1); tw.done(); }
      }
    }
    var callbacks = frameCallbacks.slice();
    for (i = 0; i < callbacks.length; i++) {
      var cb = callbacks[i];
      guard(function () { cb(dt, time); }, 'onFrame');
    }
  };

  // ---- nodes -------------------------------------------------------------------------------

  var nodes = {};

  function hostProps(props) {
    // Everything except the transform and handlers goes to the host as JSON.
    var out = {};
    for (var key in props) {
      if (key === 'position' || key === 'rotation' || key === 'scale' || key === 'parent' || key === 'onClick') continue;
      var value = props[key];
      if (value === undefined || typeof value === 'function') continue;
      if (key === 'color' || key === 'fill' || key === 'border') value = color(value);
      out[key] = value;
    }
    return out;
  }

  function Node(kind, props) {
    props = props || {};
    this.kind = kind;
    this.parent = null;
    this.children = [];
    this.data = {};
    this._p = point(props.position);
    this._q = toQuat(props.rotation);
    this._s = toScale(props.scale);
    this._props = hostProps(props);
    this._click = null;
    this._hover = null;
    this._alive = true;
    var parent = props.parent || null;
    if (parent) this._props.parent = parent.id;
    this.id = H.create(kind, JSON.stringify(this._props));
    nodes[this.id] = this;
    if (parent) { this.parent = parent; parent.children.push(this); }
    if (kind === 'text') this._measure();
    this._push();
    if (props.onClick) this.onClick(props.onClick);
  }

  Node.prototype._push = function () {
    if (!this._alive) return;
    var p = this._p, q = this._q, s = this._s;
    H.xf(this.id, p[0], p[1], p[2], q[0], q[1], q[2], q[3], s[0], s[1], s[2]);
  };
  Node.prototype._measure = function () {
    var p = this._props;
    var m = H.measure(String(p.text === undefined ? '' : p.text), p.size || theme.text.body, p.weight || 'medium', p.maxWidth || 0);
    this.width = m[0];
    this.height = m[1];
  };
  Node.prototype.set = function (props) {
    if (!this._alive) return this;
    if (props.position !== undefined) this._p = point(props.position);
    if (props.rotation !== undefined) this._q = toQuat(props.rotation);
    if (props.scale !== undefined) this._s = toScale(props.scale);
    if (props.position !== undefined || props.rotation !== undefined || props.scale !== undefined) this._push();
    var changed = hostProps(props), any = false;
    for (var key in changed) { this._props[key] = changed[key]; any = true; }
    if (any) {
      H.update(this.id, JSON.stringify(changed));
      if (this.kind === 'text') this._measure();
    }
    return this;
  };
  Node.prototype.add = function (child) {
    if (!child || child === this) return child;
    if (child.parent) { var i = child.parent.children.indexOf(child); if (i >= 0) child.parent.children.splice(i, 1); }
    child.parent = this;
    this.children.push(child);
    child._props.parent = this.id;
    H.update(child.id, JSON.stringify({ parent: this.id }));
    child._push();
    return child;
  };
  Node.prototype.remove = function () {
    if (!this._alive) return;
    var kids = this.children.slice();
    for (var i = 0; i < kids.length; i++) kids[i].remove();
    if (this.parent) { var j = this.parent.children.indexOf(this); if (j >= 0) this.parent.children.splice(j, 1); }
    this._alive = false;
    delete nodes[this.id];
    H.destroy(this.id);
  };
  Node.prototype.worldRotation = function () {
    return this.parent ? quat.mul(this.parent.worldRotation(), this._q) : this._q;
  };
  Node.prototype.worldScale = function () {
    return this.parent ? vec.mul(this.parent.worldScale(), this._s) : this._s;
  };
  Node.prototype.worldPosition = function () {
    if (!this.parent) return [this._p[0], this._p[1], this._p[2]];
    var local = vec.mul(this._p, this.parent.worldScale());
    return vec.add(this.parent.worldPosition(), quat.rotate(this.parent.worldRotation(), local));
  };
  // Turns local +Z toward a world point (content faces -Z, so use space.faceUser for readable panels).
  Node.prototype.lookAt = function (target) {
    var world = quat.look(vec.sub(point(target), this.worldPosition()));
    this.rotation = this.parent ? quat.mul(quat.inverse(this.parent.worldRotation()), world) : world;
    return this;
  };
  Node.prototype.onClick = function (fn) {
    this._click = fn;
    if (!this._props.interactive) this.set({ interactive: true });
    return this;
  };
  Node.prototype.onHover = function (fn) {
    this._hover = fn;
    if (!this._props.interactive) this.set({ interactive: true });
    return this;
  };
  // Keeps turning about a local axis. Returns a function that stops it.
  Node.prototype.spin = function (axis, degreesPerSecond) {
    var self = this;
    return scene.onFrame(function (dt) {
      if (!self._alive) return;
      self.rotation = quat.mul(self._q, quat.axisAngle(axis, degreesPerSecond * dt));
    });
  };
  // Animates any of position, scale, opacity toward target values.
  Node.prototype.animate = function (to, options) {
    var self = this;
    var fromP = this._p.slice(), fromS = this._s.slice(), fromO = this._props.opacity === undefined ? 1 : this._props.opacity;
    var toP = to.position !== undefined ? point(to.position) : null;
    var toS = to.scale !== undefined ? toScale(to.scale) : null;
    var toO = to.opacity;
    return tween((options && options.duration) || 0.3, function (t) {
      if (!self._alive) return;
      if (toP) self._p = vec.lerp(fromP, toP, t);
      if (toS) self._s = vec.lerp(fromS, toS, t);
      if (toP || toS) self._push();
      if (toO !== undefined) self.opacity = fromO + (toO - fromO) * t;
    }, options);
  };

  Object.defineProperty(Node.prototype, 'position', {
    get: function () { return [this._p[0], this._p[1], this._p[2]]; },
    set: function (v) { this._p = point(v); this._push(); }
  });
  Object.defineProperty(Node.prototype, 'rotation', {
    get: function () { return [this._q[0], this._q[1], this._q[2], this._q[3]]; },
    set: function (v) { this._q = toQuat(v); this._push(); }
  });
  Object.defineProperty(Node.prototype, 'scale', {
    get: function () { return [this._s[0], this._s[1], this._s[2]]; },
    set: function (v) { this._s = toScale(v); this._push(); }
  });
  ['color', 'opacity', 'visible', 'text', 'fill', 'border'].forEach(function (name) {
    Object.defineProperty(Node.prototype, name, {
      get: function () { return this._props[name]; },
      set: function (v) { var p = {}; p[name] = v; this.set(p); }
    });
  });

  global.__event = function (id, type) {
    var node = nodes[id];
    // Events bubble, so a click on a label's text reaches the group that owns the handler.
    while (node) {
      if (type === 'click' && node._click) { var c = node._click; guard(function () { c(node); }, 'onClick'); return true; }
      if ((type === 'enter' || type === 'exit') && node._hover) { var h = node._hover; guard(function () { h(type === 'enter', node); }, 'onHover'); return true; }
      node = node.parent;
    }
    return false;
  };

  function make(kind) {
    return function (props) { return new Node(kind, props); };
  }

  var scene = {
    // Containers and solids. Solids are lit softly from above; pass `shade: false` for flat colour and
    // `glow: true` for a see-through volume that does not hide what is behind it.
    group: make('group'),
    box: make('box'),            // { size: [x, y, z] }
    sphere: make('sphere'),      // { radius }
    cylinder: make('cylinder'),  // { radius, height, topRadius? }  along local Y
    cone: function (props) { var p = Object.assign({}, props, { topRadius: 0.0001 }); return new Node('cylinder', p); },
    torus: make('torus'),        // { radius, tube, arc? }  ring in the local XZ plane, arc in degrees
    disc: make('disc'),          // { radius }  in the local XZ plane, facing +Y
    tube: make('tube'),          // { points: [[x,y,z],...], radius, flow?: { speed, density } }
    mesh: make('mesh'),          // { positions: [x,y,z,...], indices: [a,b,c,...], colors?: ['#hex',...] }
    // Flat things that read from the -Z side. Layer text a few millimetres in front of a panel (theme.layer).
    text: make('text'),          // { text, size?, weight?: 'medium'|'semibold', align?, anchor?, maxWidth? } -> .width .height
    panel: make('panel'),        // { width, height, radius?, fill?, border?, borderWidth? }
    line: function (props) {     // { points, width? }  a thin unlit tube
      var p = Object.assign({ shade: false }, props, { radius: (props.width || 0.004) * 0.5 });
      delete p.width;
      return new Node('tube', p);
    },

    onFrame: function (fn) { frameCallbacks.push(fn); return remover(frameCallbacks, fn); },
    after: function (seconds, fn) { var t = { left: seconds, every: 0, fn: fn }; timers.push(t); return remover(timers, t); },
    every: function (seconds, fn) { var t = { left: seconds, every: seconds, fn: fn }; timers.push(t); return remover(timers, t); },
    wait: function (seconds) { return new Promise(function (resolve) { scene.after(seconds, resolve); }); },
    tween: tween,
    get time() { return clock; }
  };

  // ---- the space ---------------------------------------------------------------------------

  function parse(json) { return json ? JSON.parse(json) : null; }

  var pendingFinds = {}, nextFind = 1;
  global.__resolve = function (id, json) {
    var done = pendingFinds[id];
    delete pendingFinds[id];
    if (done) done(parse(json) || []);
  };

  var space = {
    // The camera frame that came with this request, or null when the request had none.
    // view.point(u, v): what is at that spot in the image (u, v from 0 to 1, origin top-left), as a world-space
    // hit { position, normal, distance }, or null if nothing solid was found along that line of sight.
    view: null,

    // Casts a ray into the live geometry the device senses right now. Returns { position, normal, distance } or null.
    raycast: function (origin, direction, maxDistance) {
      var o = point(origin), d = vec.normalize(direction);
      return parse(H.raycast(o[0], o[1], o[2], d[0], d[1], d[2], maxDistance || 10));
    },
    // The floor directly under a point. Falls back to the device's floor estimate when the ray finds nothing.
    floorBelow: function (p) {
      p = point(p);
      var hit = space.raycast([p[0], p[1] + 0.05, p[2]], [0, -1, 0], 4);
      return hit && hit.normal[1] > 0.7 ? hit.position : [p[0], space.floorY, p[2]];
    },
    get floorY() { return H.floorY(); },
    // Flat surfaces the device has found so far: [{ id, kind, label, position, rotation, size }]. Walls face into
    // the room along their local +Z. The list grows as the user looks around; it can be empty.
    surfaces: function () { return parse(H.surfaces()) || []; },
    // Virtual objects the app itself has placed (for example a stand-in machine): [{ id, name, aliases, about,
    // position, rotation, size, actionPoint? }].
    props: function () { return parse(H.props()) || []; },
    prop: function (name) {
      var wanted = normalize(name), best = null, bestScore = 0;
      space.props().forEach(function (p) {
        var names = [p.name, p.id].concat(p.aliases || []);
        names.forEach(function (n) {
          var candidate = normalize(n), score = 0;
          if (candidate === wanted) score = 1000 + candidate.length;
          else if (hasWords(wanted, candidate)) score = 500 + candidate.length;
          else if (hasWords(candidate, wanted)) score = 200 + wanted.length;
          if (score > bestScore) { bestScore = score; best = p; }
        });
      });
      return best;
    },
    // Looks at the scene again, now, and locates what you describe. Resolves to an array of hits, best first,
    // each { position, normal, distance, label }. Use this for things that were not in the request's view, or
    // to re-find something after the user has moved.
    find: function (description) {
      return new Promise(function (resolve) {
        var id = nextFind++;
        pendingFinds[id] = resolve;
        H.find(String(description), id);
      });
    },
    // Rotation for flat content at `position` so it reads from where the user is standing. Content faces -Z.
    faceUser: function (position, maxPitchDegrees) {
      var p = point(position), head = user.head.position;
      var away = vec.sub(p, head), flat = vec.flat(away);
      if (vec.length(flat) < 1e-4) return quat.look(vec.flat(user.head.forward));
      var yaw = quat.look(vec.normalize(flat));
      if (!maxPitchDegrees) return yaw;
      var pitch = Math.atan2(-away[1], vec.length(flat)) / DEG;
      pitch = Math.max(-maxPitchDegrees, Math.min(maxPitchDegrees, pitch));
      return quat.mul(yaw, quat.axisAngle([1, 0, 0], pitch));
    },
    // A spot beside something, on the user's side and a little below its centre, so a card placed there does
    // not cover the thing it is about. `thing` is a prop, a hit, or { position, size }.
    beside: function (thing, gap) {
      var p = point(thing), s = thing.size || [0.2, 0.2, 0.2], head = user.head.position;
      var toUser = vec.normalize(vec.flat(vec.sub(head, p)));
      if (vec.length(toUser) < 1e-4) toUser = [0, 0, -1];
      var reach = Math.max(s[0], s[2]) * 0.5 + (gap === undefined ? 0.28 : gap);
      var spot = vec.add(p, vec.scale(toUser, reach));
      spot[1] = Math.max(space.floorY + 0.35, Math.min(space.floorY + 1.5, p[1] - s[1] * 0.25));
      return spot;
    },
    // The top centre of something that has a size.
    topOf: function (thing) {
      var s = thing.size || [0, 0, 0];
      return vec.add(point(thing), quat.rotate(toQuat(thing.rotation), [0, s[1] / 2, 0]));
    },
    // A spot of free floor near a point, on the user's side of it, clear of props and walls. `radius` is how much
    // room the content needs.
    freeFloorNear: function (target, radius, reach) {
      var p = point(target), head = user.head.position;
      radius = radius || 0.3;
      var toUser = vec.normalize(vec.flat(vec.sub(head, p)));
      if (vec.length(toUser) < 1e-4) toUser = [0, 0, -1];
      var obstacles = space.props(), walls = space.surfaces().filter(function (s) { return s.kind === 'wall'; });
      var angles = [0, 35, -35, 70, -70, 110, -110, 150, -150, 180];
      var best = null, bestClear = -Infinity;
      for (var i = 0; i < angles.length; i++) {
        var dir = quat.rotate(quat.axisAngle([0, 1, 0], angles[i]), toUser);
        var spot = vec.add(p, vec.scale(dir, (reach === undefined ? 0.35 : reach) + radius));
        spot[1] = space.floorY;
        var clear = Infinity;
        obstacles.forEach(function (o) {
          if (o.position[1] - o.size[1] * 0.5 > space.floorY + 0.6) return;   // mounted above the floor
          var gap = vec.length(vec.flat(vec.sub(spot, o.position))) - radius - 0.5 * Math.sqrt(o.size[0] * o.size[0] + o.size[2] * o.size[2]) * 0.8;
          if (gap < clear) clear = gap;
        });
        walls.forEach(function (w) {
          var local = quat.rotate(quat.inverse(w.rotation), vec.sub(spot, w.position));
          if (Math.abs(local[0]) > w.size[0] * 0.5 + radius) return;
          if (local[2] - radius < clear) clear = local[2] - radius;
        });
        if (clear >= 0) return spot;
        if (clear > bestClear) { bestClear = clear; best = spot; }
      }
      return best;
    },
    // The wall closest to a point, with the point on it nearest to the target: { surface, position, rotation }.
    nearestWall: function (target) {
      var p = point(target), best = null, bestD = Infinity;
      space.surfaces().forEach(function (w) {
        if (w.kind !== 'wall') return;
        var local = quat.rotate(quat.inverse(w.rotation), vec.sub(p, w.position));
        var slide = Math.max(0, Math.abs(local[0]) - w.size[0] * 0.5);
        var d = Math.abs(local[2]) + slide;
        if (d >= bestD) return;
        bestD = d;
        var half = Math.max(0, w.size[0] * 0.5 - 0.3);
        var on = [Math.max(-half, Math.min(half, local[0])), local[1], 0.02];
        best = { surface: w, position: vec.add(w.position, quat.rotate(w.rotation, on)), rotation: quat.look(vec.scale(quat.rotate(w.rotation, [0, 0, 1]), -1)) };
      });
      return best;
    }
  };

  if (H.hasView()) {
    space.view = {
      ray: function (u, v) { return parse(H.viewRay(u, v)); },
      point: function (u, v) {
        var ray = parse(H.viewRay(u, v));
        return ray ? space.raycast(ray.origin, ray.direction, 12) : null;
      }
    };
  }

  var user = {
    // Where the user's head is right now: { position, forward, rotation }.
    get head() {
      var a = H.user();
      return { position: [a[0], a[1], a[2]], forward: [a[3], a[4], a[5]], rotation: [a[6], a[7], a[8], a[9]] };
    }
  };

  // ---- voice -------------------------------------------------------------------------------

  function normalize(s) {
    return String(s || '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();
  }
  function hasWords(text, phrase) {
    return phrase.length > 0 && (' ' + text + ' ').indexOf(' ' + phrase + ' ') >= 0;
  }

  var voiceHandlers = [];
  var voice = {
    // Handles a short spoken command on the device, instantly, without going back to the planner.
    // voice.on(['next', 'continue'], () => goTo(step + 1))
    on: function (phrases, handler) {
      var list = (Array.isArray(phrases) ? phrases : [phrases]).map(normalize);
      var entry = { phrases: list, handler: handler };
      voiceHandlers.push(entry);
      return remover(voiceHandlers, entry);
    }
  };
  global.__voice = function (text) {
    var said = normalize(text);
    // Only short utterances are commands; a full sentence that happens to contain "next" goes to the planner.
    if (said.split(' ').length > 4) return false;
    for (var i = voiceHandlers.length - 1; i >= 0; i--) {
      var entry = voiceHandlers[i];
      for (var j = 0; j < entry.phrases.length; j++) {
        if (hasWords(said, entry.phrases[j])) { var h = entry.handler; guard(function () { h(said); }, 'voice'); return true; }
      }
    }
    return false;
  };

  // ---- kit: ready-made pieces in Spatail's visual language ------------------------------------
  // Nothing here is special: every piece is built from the scene primitives above, and programs are free to
  // build their own instead.

  // Kit pieces are groups. Only placement goes to the group itself; style, size and colour belong to the
  // pieces inside it.
  function placementOf(options) {
    var out = {};
    ['position', 'rotation', 'scale', 'parent', 'billboard', 'visible'].forEach(function (k) {
      if (options[k] !== undefined) out[k] = options[k];
    });
    return out;
  }

  var kit = {
    // A rounded pill of text. style 'solid' (coloured, white text) or 'outline' (white, coloured text).
    pill: function (text, options) {
      options = options || {};
      var c = color(options.color), solid = options.style !== 'outline';
      var size = options.size || theme.text.label, padX = size * 0.75, height = size * 1.8;
      var g = scene.group(placementOf(options));
      var label = scene.text({ parent: g, text: text, size: size, weight: 'semibold', align: 'center', color: solid ? '#FFFFFF' : c, position: [0, 0, -theme.layer] });
      g.width = label.width + padX * 2;
      g.height = height;
      scene.panel({ parent: g, width: g.width, height: height, radius: height / 2, fill: solid ? c : theme.surface, border: c });
      g.label = label;
      return g;
    },
    // A pill on a short stem, standing on a point and turning to face the user.
    label: function (text, at, options) {
      options = options || {};
      var c = color(options.color), stem = options.stem === undefined ? 0.085 : options.stem;
      var g = scene.group({ position: point(at), billboard: 'upright' });
      if (stem > 0) {
        scene.line({ parent: g, points: [[0, 0, 0], [0, stem, 0]], width: 0.003, color: c });
        scene.sphere({ parent: g, radius: 0.007, color: c, shade: false });
      }
      var pill = kit.pill(text, { color: c, style: options.style || 'outline', size: options.size });
      g.add(pill);
      pill.position = [0, stem + pill.height / 2, 0];
      g.pill = pill;
      return g;
    },
    // A numbered (or lettered) disc on a stem.
    pin: function (text, at, options) {
      options = options || {};
      var c = color(options.color), stem = options.stem === undefined ? 0.14 : options.stem, d = 0.062;
      var g = scene.group({ position: point(at), billboard: 'upright' });
      scene.line({ parent: g, points: [[0, 0, 0], [0, stem, 0]], width: 0.0035, color: c });
      scene.sphere({ parent: g, radius: 0.008, color: c, shade: false });
      var head = scene.group({ parent: g, position: [0, stem + d / 2, 0] });
      scene.panel({ parent: head, width: d, height: d, radius: d / 2, fill: c, border: '#FFFFFF', borderWidth: 0.003 });
      scene.text({ parent: head, text: String(text), size: 0.034, weight: 'semibold', align: 'center', color: '#FFFFFF', position: [0, 0, -theme.layer] });
      g.head = head;
      return g;
    },
    // A small rounded button.
    button: function (text, onClick, options) {
      options = options || {};
      var c = color(options.color), primary = options.primary !== false;
      var size = options.size || theme.text.small, height = size * 2;
      var g = scene.group(placementOf(options));
      var label = scene.text({ parent: g, text: text, size: size, weight: 'semibold', align: 'center', color: primary ? '#FFFFFF' : c, position: [0, 0, -theme.layer] });
      g.width = label.width + size * 2;
      g.height = height;
      scene.panel({ parent: g, width: g.width, height: height, radius: height / 2, fill: primary ? c : theme.surface, border: c });
      g.onClick(onClick);
      g.onHover(function (inside) { g.scale = inside ? 1.05 : 1; });
      return g;
    },
    // A card: optional number in a ring, title, body text and a row of buttons. Centred on its position.
    // kit.card({ title, body, number, width, color, buttons: [{ text, onClick, primary }], footnote })
    card: function (options) {
      options = options || {};
      var c = color(options.color), width = options.width || 0.36, pad = 0.022, ring = 0.046;
      var g = scene.group({ position: options.position, rotation: options.rotation, billboard: options.billboard });
      var hasNumber = options.number !== undefined && options.number !== null;
      var textLeft = -width / 2 + pad + (hasNumber ? ring + 0.014 : 0);
      var title = scene.text({ parent: g, text: options.title || '', size: theme.text.title, weight: 'semibold', color: options.titleColor || c, anchor: 'top', maxWidth: width / 2 - pad - textLeft });
      var rowHeight = Math.max(hasNumber ? ring : 0, title.height);
      var body = options.body ? scene.text({ parent: g, text: options.body, size: theme.text.body, color: theme.ink, anchor: 'top', maxWidth: width - pad * 2 }) : null;
      var buttons = options.buttons || [];
      var footer = buttons.length || options.footnote ? 0.036 + 0.018 : 0;
      var height = pad + rowHeight + (body ? body.height + 0.016 : 0) + footer + pad;
      var top = height / 2;
      scene.panel({ parent: g, width: width, height: height, radius: 0.022, fill: theme.surface, border: c });
      if (hasNumber) {
        var n = scene.group({ parent: g, position: [-width / 2 + pad + ring / 2, top - pad - rowHeight / 2, -theme.layer] });
        scene.panel({ parent: n, width: ring, height: ring, radius: ring / 2, fill: theme.surface, border: c, borderWidth: 0.003 });
        scene.text({ parent: n, text: String(options.number), size: 0.026, weight: 'semibold', align: 'center', color: c, position: [0, 0, -theme.layer] });
      }
      title.position = [textLeft, top - pad - (rowHeight - title.height) / 2, -theme.layer];
      if (body) body.position = [-width / 2 + pad, top - pad - rowHeight - 0.016, -theme.layer];
      var y = -top + pad + 0.018, x = width / 2 - pad;
      for (var i = buttons.length - 1; i >= 0; i--) {
        var b = kit.button(buttons[i].text, buttons[i].onClick, { color: c, primary: buttons[i].primary !== false });
        g.add(b);
        b.position = [x - b.width / 2, y, -theme.layer];
        x -= b.width + 0.01;
      }
      if (options.footnote) scene.text({ parent: g, text: options.footnote, size: theme.text.small, color: theme.muted, position: [-width / 2 + pad, y, -theme.layer] });
      g.width = width;
      g.height = height;
      return g;
    },
    // A circular arrow lying in the local XZ plane that keeps turning: the "turn this" cue.
    arcArrow: function (options) {
      options = options || {};
      var c = color(options.color), r = options.radius || 0.14, arc = options.arc || 250, clockwise = options.clockwise !== false;
      var g = scene.group(placementOf(options));
      var spinner = scene.group({ parent: g });
      scene.torus({ parent: spinner, radius: r, tube: 0.011, arc: arc, color: c });
      // The arc runs anticlockwise (seen from above) from +X. The head leads the motion.
      var a = (clockwise ? 0 : arc) * DEG;
      var radial = [Math.cos(a), 0, Math.sin(a)];
      var tangent = vec.scale([-Math.sin(a), 0, Math.cos(a)], clockwise ? -1 : 1);
      scene.cone({ parent: spinner, radius: 0.032, height: 0.07, color: c, position: vec.add(vec.scale(radial, r), vec.scale(tangent, 0.035)), rotation: quat.fromTo([0, 1, 0], tangent) });
      // Seen from above, positive rotation about +Y is clockwise.
      g.stop = spinner.spin([0, 1, 0], (clockwise ? 1 : -1) * (options.speed || 80));
      return g;
    },
    // A straight 3D arrow between two points.
    arrow: function (from, to, options) {
      options = options || {};
      var a = point(from), b = point(to), c = color(options.color), r = options.radius || 0.01;
      var dir = vec.normalize(vec.sub(b, a)), head = Math.min(0.08, vec.distance(a, b) * 0.4);
      var g = scene.group();
      scene.tube({ parent: g, points: [a, vec.sub(b, vec.scale(dir, head))], radius: r, color: c });
      scene.cone({ parent: g, radius: r * 2.6, height: head, color: c, position: vec.sub(b, vec.scale(dir, head / 2)), rotation: quat.fromTo([0, 1, 0], dir) });
      return g;
    },
    // A pulsing see-through volume around something that has a size (a prop, or { position, size, rotation }).
    glow: function (thing, options) {
      options = options || {};
      var c = color(options.color), s = thing.size || [0.2, 0.2, 0.2], k = options.inflate || 1.08;
      // Round parts get a cylinder along their long axis; boxy ones a box.
      var axis = -1, longest = 0;
      for (var i = 0; i < 3; i++) {
        var a = s[(i + 1) % 3], b = s[(i + 2) % 3];
        if (Math.abs(a - b) / Math.max(a, b) <= 0.3 && s[i] > longest) { longest = s[i]; axis = i; }
      }
      var g = scene.group({ position: point(thing), rotation: thing.rotation });
      var volume = axis < 0
        ? scene.box({ parent: g, size: vec.scale(s, k), color: c, glow: true, opacity: 0.3 })
        : scene.cylinder({ parent: g, radius: Math.max(s[(axis + 1) % 3], s[(axis + 2) % 3]) * k / 2, height: s[axis] * 1.02, color: c, glow: true, opacity: 0.3,
            rotation: axis === 0 ? [0, 0, 90] : (axis === 2 ? [90, 0, 0] : [0, 0, 0]) });
      scene.onFrame(function (dt, time) { volume.opacity = 0.2 + 0.18 * (0.5 + 0.5 * Math.sin(time * 3.2)); });
      return g;
    },
    // Pops a node in from nothing. Returns the tween's promise.
    appear: function (node, options) {
      var target = node.scale;
      node.scale = 0.0001;
      return node.animate({ scale: target }, Object.assign({ duration: 0.28, ease: 'back' }, options));
    }
  };

  // ---- program-facing globals ------------------------------------------------------------------

  var state = {};
  if (global.__initialState) {
    try { state = JSON.parse(global.__initialState) || {}; } catch (e) { state = {}; }
  }
  global.__saveState = function () {
    try { return JSON.stringify(state); } catch (e) { return '{}'; }
  };

  function log() {
    var parts = [];
    for (var i = 0; i < arguments.length; i++) {
      var a = arguments[i];
      parts.push(typeof a === 'string' ? a : (function () { try { return JSON.stringify(a); } catch (e) { return String(a); } })());
    }
    H.log('info', parts.join(' '));
  }

  global.vec = vec;
  global.quat = quat;
  global.theme = theme;
  global.scene = scene;
  global.space = space;
  global.user = user;
  global.voice = voice;
  global.kit = kit;
  // Values that should survive when this experience is replaced by a new version of itself (the current step,
  // which items are ticked). Plain JSON only.
  global.state = state;
  global.log = log;
  global.console = { log: log, warn: log, error: function () { H.log('error', Array.prototype.join.call(arguments, ' ')); } };
  // Call when the experience has served its purpose and should take itself out of the room.
  global.finish = function () { H.finish(); };
})(typeof globalThis !== 'undefined' ? globalThis : this);
