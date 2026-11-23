/*
 * Spatail docs simulator.
 *
 * Replays what the shipping C# planner and layout solver produce (docs/sim/data/*.json, written by
 * `dotnet run -- export` in tests/Spatail.Core.Tests) in a small Three.js scene. It is a viewer for the
 * same wire format the Unity apps render, not a second implementation of the planner or the solver.
 *
 * Plain script, no bundler. Expects a global THREE (r128).
 *
 *   const sim = SpatailSim.create(container, { room, plans, theme: 'light' });
 *   sim.applyPlan(plans[1].plan, plans[1].placements);
 *
 * All input data is in Unity coordinates (left-handed, Y up, metres); it is converted to Three's
 * right-handed space at the data boundary: position (x,y,z) -> (x,y,-z), quaternion (x,y,z,w) -> (-x,-y,z,w).
 */
(function () {
  'use strict';

  var FONT = "Inter, 'Inter Variable', system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif";
  var TEX_SCALE = 2; // canvas pixels per design pixel (1 design px = 1 mm in the scene)

  var THEMES = {
    light: {
      bgTop: '#FFFFFF', bgBottom: '#ECE8FA', floor: '#F3F0FB', grid: '#DAD4F0', wall: '#FAF9FE', wallLine: '#E7E2F6',
      body: '#FFFFFF', shade: '#E7E3F4', dark: '#5A5672', glass: '#CFC9F2', edge: '#9C95C4',
      panel: '#FFFFFF', panelAlt: '#F4F2FC', ink: '#17161D', muted: '#6F6B82', onAccent: '#FFFFFF',
      accent: '#6C5CE7', accentText: '#6C5CE7', accentSoft: '#E6E2FB',
      warning: '#E8722C', warningText: '#C85A18', warningSoft: '#FCE9DB', success: '#2E9E6B',
      hemiSky: 0xffffff, hemiGround: 0xcfc8ec, hemi: 0.92, sun: 0.22
    },
    dark: {
      bgTop: '#191822', bgBottom: '#0E0D14', floor: '#1A1924', grid: '#2B293B', wall: '#15141D', wallLine: '#22202F',
      body: '#312F42', shade: '#282637', dark: '#14131C', glass: '#4A4580', edge: '#9A90F2',
      panel: '#211F2D', panelAlt: '#2A2839', ink: '#F3F1FB', muted: '#A7A2BC', onAccent: '#FFFFFF',
      accent: '#6C5CE7', accentText: '#A79DF7', accentSoft: '#2D2A4A',
      warning: '#E8722C', warningText: '#F39A62', warningSoft: '#3B2619', success: '#3DBE84',
      hemiSky: 0xffffff, hemiGround: 0x6a64a0, hemi: 0.95, sun: 0.3
    }
  };

  // ---------------------------------------------------------------------------------------------
  // Unity -> Three conversion
  // ---------------------------------------------------------------------------------------------

  function v3(a) { return new THREE.Vector3(a[0], a[1], -a[2]); }
  function quat(a) { return new THREE.Quaternion(-a[0], -a[1], a[2], a[3]); }
  function eulerDeg(a) {
    var d = Math.PI / 180;
    return new THREE.Euler(-(a[0] || 0) * d, -(a[1] || 0) * d, (a[2] || 0) * d, 'YXZ');
  }

  function clamp(v, a, b) { return v < a ? a : (v > b ? b : v); }
  function easeOut(t) { return 1 - Math.pow(1 - t, 3); }
  function easeBack(t) { var c = 1.4; return 1 + (c + 1) * Math.pow(t - 1, 3) + c * Math.pow(t - 1, 2); }
  function easeInOut(t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; }

  // ---------------------------------------------------------------------------------------------
  // Canvas helpers (all drawing is in design px; 1 px = 1 mm in the scene)
  // ---------------------------------------------------------------------------------------------

  function newCanvas(w, h) {
    var c = document.createElement('canvas');
    c.width = Math.max(2, Math.ceil(w * TEX_SCALE));
    c.height = Math.max(2, Math.ceil(h * TEX_SCALE));
    var ctx = c.getContext('2d');
    ctx.scale(TEX_SCALE, TEX_SCALE);
    ctx.textBaseline = 'middle';
    return { canvas: c, ctx: ctx, w: w, h: h };
  }

  var measureCtx = null;
  function measure(text, font) {
    if (!measureCtx) measureCtx = document.createElement('canvas').getContext('2d');
    measureCtx.font = font;
    return measureCtx.measureText(text).width;
  }

  function font(weight, size) { return weight + ' ' + size + 'px ' + FONT; }

  function roundRect(ctx, x, y, w, h, r) {
    r = Math.min(r, w / 2, h / 2);
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
  }

  function wrap(text, fnt, maxWidth) {
    var words = String(text || '').split(/\s+/), lines = [], line = '';
    for (var i = 0; i < words.length; i++) {
      var probe = line ? line + ' ' + words[i] : words[i];
      if (line && measure(probe, fnt) > maxWidth) { lines.push(line); line = words[i]; }
      else line = probe;
    }
    if (line) lines.push(line);
    return lines;
  }

  function styleColors(T, style) {
    if (style === 'warning') return { main: T.warning, text: T.warningText, soft: T.warningSoft };
    if (style === 'success') return { main: T.success, text: T.success, soft: T.accentSoft };
    return { main: T.accent, text: T.accentText, soft: T.accentSoft };
  }

  // ---------------------------------------------------------------------------------------------
  // Simulator
  // ---------------------------------------------------------------------------------------------

  function create(container, opts) {
    opts = opts || {};
    if (typeof THREE === 'undefined') throw new Error('SpatailSim needs THREE (r128) to be loaded first.');
    var room = opts.room || { entities: [], user: { position: [0, 1.6, -2.3], forward: [0, 0, 1] } };
    var plans = opts.plans || [];
    var themeName = opts.theme === 'dark' ? 'dark' : 'light';
    var T = THEMES[themeName];
    // Content is drawn larger than life so it stays legible when the whole room is on screen.
    var UI = opts.uiScale > 0 ? opts.uiScale : 1.5;
    var MM = 0.001 * UI;
    var faceCamera = opts.faceCamera !== false;
    var twinMode = 'solid';
    var disposed = false;
    var listeners = [];
    var builds = [];           // [{ id, kind, title, stepCount, currentStep, elements, placements, views: [] }]
    var dying = [];            // views fading out
    var entityById = {};
    (room.entities || []).forEach(function (e) { entityById[e.id] = e; });

    // ---- renderer -----------------------------------------------------------------------------

    var renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setClearColor(0x000000, 0);
    var canvas = renderer.domElement;
    canvas.style.display = 'block';
    canvas.style.width = '100%';
    canvas.style.height = '100%';
    canvas.style.touchAction = opts.touchAction || 'pan-y';
    canvas.style.outline = 'none';
    canvas.setAttribute('role', 'img');
    canvas.setAttribute('aria-label', 'Interactive 3D view of the Spatail pump station');
    if (getComputedStyle(container).position === 'static') container.style.position = 'relative';
    container.appendChild(canvas);
    var maxAniso = renderer.capabilities.getMaxAnisotropy ? renderer.capabilities.getMaxAnisotropy() : 1;

    var scene = new THREE.Scene();
    var camera = new THREE.PerspectiveCamera(28, 1, 0.05, 60);
    var hemi = new THREE.HemisphereLight(T.hemiSky, T.hemiGround, T.hemi);
    var sun = new THREE.DirectionalLight(0xffffff, T.sun);
    sun.position.set(-2, 5, 4);
    scene.add(hemi, sun);

    var roomGroup = new THREE.Group();
    var twinGroup = new THREE.Group();
    var contentGroup = new THREE.Group();
    scene.add(roomGroup, twinGroup, contentGroup);

    var disposables = new Set();
    function track(o) { disposables.add(o); return o; }
    function release(o) { if (o) { disposables.delete(o); if (o.dispose) o.dispose(); } }

    // ---- materials ----------------------------------------------------------------------------

    function basic(color, opacity, extra) {
      var m = new THREE.MeshBasicMaterial(Object.assign({ color: new THREE.Color(color), transparent: true, opacity: opacity == null ? 1 : opacity }, extra || {}));
      m.userData.base = m.opacity;
      return track(m);
    }
    function lineMat(color, opacity) {
      var m = new THREE.LineBasicMaterial({ color: new THREE.Color(color), transparent: true, opacity: opacity == null ? 1 : opacity });
      m.userData.base = m.opacity;
      return track(m);
    }
    function texMat(tex, extra) {
      var m = new THREE.MeshBasicMaterial(Object.assign({ map: tex, transparent: true, alphaTest: 0.01, depthWrite: false, side: THREE.DoubleSide }, extra || {}));
      m.userData.base = 1;
      return track(m);
    }
    function texture(canvasEl) {
      var t = new THREE.CanvasTexture(canvasEl);
      t.anisotropy = maxAniso;
      t.minFilter = THREE.LinearMipmapLinearFilter;
      t.generateMipmaps = true;
      return track(t);
    }
    function geo(g) { return track(g); }

    /** A plane showing a canvas, sized in metres from its design px size. */
    function canvasPlane(cv, extra) {
      var mesh = new THREE.Mesh(geo(new THREE.PlaneGeometry(cv.w * MM, cv.h * MM)), texMat(texture(cv.canvas), extra));
      mesh.userData.cv = cv;
      return mesh;
    }

    // ---- room ---------------------------------------------------------------------------------

    function buildRoom() {
      clearGroup(roomGroup);
      (room.entities || []).forEach(function (e) {
        if (e.kind === 'floor') {
          var w = e.size[0], d = e.size[2];
          var g = new THREE.Group();
          g.position.copy(v3(e.position));
          g.quaternion.copy(quat(e.rotation));
          var plane = new THREE.Mesh(geo(new THREE.PlaneGeometry(w, d)), basic(T.floor, 1, { transparent: false }));
          plane.rotation.x = -Math.PI / 2;
          plane.position.y = -0.004;
          g.add(plane);
          var pts = [], step = 0.5, x, z;
          for (x = -w / 2; x <= w / 2 + 1e-6; x += step) pts.push(x, 0, -d / 2, x, 0, d / 2);
          for (z = -d / 2; z <= d / 2 + 1e-6; z += step) pts.push(-w / 2, 0, z, w / 2, 0, z);
          var lg = geo(new THREE.BufferGeometry());
          lg.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
          g.add(new THREE.LineSegments(lg, lineMat(T.grid, 0.9)));
          roomGroup.add(g);
        } else if (e.kind === 'wall') {
          var ww = e.size[0], wh = e.size[1];
          var wg = new THREE.Group();
          wg.position.copy(v3(e.position));
          wg.quaternion.copy(quat(e.rotation));
          // Unity-local +Z (into the room) is three-local -Z, so the visible face must look down -Z.
          var wall = new THREE.Mesh(geo(new THREE.PlaneGeometry(ww, wh)), basic(T.wall, 1, { transparent: false, side: THREE.FrontSide }));
          wall.rotation.y = Math.PI;
          wg.add(wall);
          var lp = [], lx;
          for (lx = -ww / 2; lx <= ww / 2 + 1e-6; lx += 1.0) lp.push(lx, -wh / 2, -0.004, lx, wh / 2, -0.004);
          lp.push(-ww / 2, -wh / 2, -0.004, ww / 2, -wh / 2, -0.004);
          var wlg = geo(new THREE.BufferGeometry());
          wlg.setAttribute('position', new THREE.Float32BufferAttribute(lp, 3));
          var lines = new THREE.LineSegments(wlg, lineMat(T.wallLine, 1));
          wg.add(lines);
          wg.userData.wall = { normal: new THREE.Vector3(0, 0, -1).applyQuaternion(wg.quaternion), center: wg.position.clone() };
          roomGroup.add(wg);
        }
      });
    }

    // ---- twin ---------------------------------------------------------------------------------

    var twinMats = null;

    function buildTwin() {
      clearGroup(twinGroup);
      twinMats = {
        body: track(new THREE.MeshLambertMaterial({ color: new THREE.Color(T.body) })),
        shade: track(new THREE.MeshLambertMaterial({ color: new THREE.Color(T.shade) })),
        dark: track(new THREE.MeshLambertMaterial({ color: new THREE.Color(T.dark) })),
        glass: track(new THREE.MeshLambertMaterial({ color: new THREE.Color(T.glass), transparent: true, opacity: 0.5 })),
        edge: track(new THREE.LineBasicMaterial({ color: new THREE.Color(T.edge), transparent: true, opacity: themeName === 'dark' ? 0.85 : 0.9 }))
      };
      Object.keys(twinMats).forEach(function (k) { twinMats[k].userData.base = twinMats[k].opacity; });

      (room.entities || []).forEach(function (e) {
        if (!e.shapes || !e.shapes.length) return;
        var g = new THREE.Group();
        g.position.copy(v3(e.shapeOrigin || e.position));
        g.quaternion.copy(quat(e.rotation));
        g.userData.entity = e.id;
        e.shapes.forEach(function (s) {
          var geom, size = s.s || [1, 1, 1], threshold = 28;
          if (s.t === 'cyl') {
            var top = size[2] > 0 ? size[2] : size[0];
            geom = new THREE.CylinderGeometry(top, size[0], size[1], size[0] > 0.1 ? 40 : 24);
          } else if (s.t === 'torus') {
            geom = new THREE.TorusGeometry(size[0], size[1], 10, 40);
            geom.rotateX(Math.PI / 2);
            threshold = 180; // no edge lines on a torus
          } else if (s.t === 'sphere') {
            geom = new THREE.SphereGeometry(size[0], 24, 16);
            threshold = 180;
          } else {
            geom = new THREE.BoxGeometry(size[0], size[1], size[2]);
          }
          geo(geom);
          var mesh = new THREE.Mesh(geom, twinMats[s.tone] || twinMats.body);
          mesh.position.copy(v3(s.p || [0, 0, 0]));
          if (s.r) mesh.rotation.copy(eulerDeg(s.r));
          g.add(mesh);
          if (threshold < 180 && s.tone !== 'dark') {
            var edges = new THREE.LineSegments(geo(new THREE.EdgesGeometry(geom, threshold)), twinMats.edge);
            edges.position.copy(mesh.position);
            edges.rotation.copy(mesh.rotation);
            g.add(edges);
          }
        });
        twinGroup.add(g);
      });
      applyTwinMode();
    }

    function applyTwinMode() {
      if (!twinMats) return;
      twinGroup.visible = twinMode !== 'hidden';
      var ghost = twinMode === 'ghost';
      ['body', 'shade', 'dark', 'glass'].forEach(function (k) {
        var m = twinMats[k];
        m.transparent = ghost || k === 'glass';
        m.opacity = ghost ? 0.14 : m.userData.base;
        m.depthWrite = !ghost;
        m.needsUpdate = true;
      });
      twinMats.edge.opacity = ghost ? 0.55 : twinMats.edge.userData.base;
    }

    // ---- element views ------------------------------------------------------------------------
    //
    // A view is { group, el, build, key, appear (0..1), wanted (bool), update(dt, time), click targets }.
    // group sits at the element's placement position; billboarded children re-orient themselves each frame.

    var billboards = [];   // meshes that copy the camera orientation
    var clickables = [];   // meshes with userData.onClick(uv)

    function placementFor(build, el) {
      var p = build.placements && build.placements[build.id + '/' + el.id];
      if (p) return p;
      // Fallback for plans that arrive without solver output: sit on (or at) the anchor entity.
      var ent = entityById[el.anchor && el.anchor.entity];
      var pos = ent ? ent.position.slice() : (room.user ? [room.user.position[0], 1.3, room.user.position[2] + 1] : [0, 1.3, 0]);
      var top = el.anchor && (el.anchor.at === 'top' || el.anchor.at === 'above' || el.anchor.at === 'surface');
      if (ent && top) pos[1] += ent.size[1] / 2 + (el.anchor.at === 'above' ? 0.3 : 0);
      if (ent && el.anchor && el.anchor.at === 'floor-near') { pos[1] = room.floorY || 0; pos[2] -= ent.size[2] / 2 + 0.4; }
      return { position: pos, rotation: [0, 0, 0, 1], target: ent ? ent.position : pos, pivotBottom: !!top };
    }

    function makeView(build, el) {
      var pl = placementFor(build, el);
      var view = {
        el: el, build: build, placement: pl, group: new THREE.Group(), appear: 0, wanted: true,
        updaters: [], own: [], noScale: false, scale: el.scale > 0 ? el.scale : 1
      };
      view.group.position.copy(v3(pl.position));
      var maker = MAKERS[el.type];
      if (maker) maker(view, pl);
      view.group.traverse(function (o) {
        if (o.userData && o.userData.billboard) billboards.push(o);
        if (o.userData && o.userData.onClick) { o.userData.view = view; clickables.push(o); }
      });
      contentGroup.add(view.group);
      setViewProgress(view, 0);
      return view;
    }

    function setViewProgress(view, a) {
      var s = view.noScale ? 1 : (0.6 + 0.4 * easeBack(clamp(a, 0, 1)));
      view.group.scale.setScalar(s);
      var fade = easeOut(clamp(a, 0, 1));
      view.group.traverse(function (o) {
        var m = o.material;
        if (m && m.userData && m.userData.base != null && !m.userData.shared) m.opacity = m.userData.base * fade * (m.userData.pulse == null ? 1 : m.userData.pulse);
      });
      view.group.visible = a > 0.001;
    }

    function removeView(view) {
      contentGroup.remove(view.group);
      view.group.traverse(function (o) {
        var i = billboards.indexOf(o); if (i >= 0) billboards.splice(i, 1);
        i = clickables.indexOf(o); if (i >= 0) clickables.splice(i, 1);
        if (o.geometry) release(o.geometry);
        if (o.material) { if (o.material.map) release(o.material.map); release(o.material); }
      });
    }

    function emit(evt) { listeners.slice().forEach(function (cb) { try { cb(evt); } catch (e) { /* listener errors are theirs */ } }); }

    // -- label ----------------------------------------------------------------------------------

    function drawPill(text, style) {
      var c = styleColors(T, style), f = font(600, 44), padX = 30, h = 84;
      var badge = style === 'warning' ? 52 : 0;
      var w = Math.ceil(measure(text, f)) + padX * 2 + (badge ? badge + 8 : 0);
      var cv = newCanvas(w + 8, h + 8), ctx = cv.ctx;
      ctx.translate(4, 4);
      var solid = style === 'accent' || style === 'success';
      roundRect(ctx, 0, 0, w, h, h / 2);
      ctx.fillStyle = solid ? c.main : T.panel;
      ctx.fill();
      ctx.lineWidth = 4;
      ctx.strokeStyle = c.main;
      ctx.stroke();
      var tx = padX;
      if (badge) {
        ctx.beginPath(); ctx.arc(16 + badge / 2, h / 2, badge / 2, 0, Math.PI * 2);
        ctx.fillStyle = c.main; ctx.fill();
        ctx.fillStyle = '#FFFFFF'; ctx.font = font(700, 38); ctx.textAlign = 'center';
        ctx.fillText('!', 16 + badge / 2, h / 2 + 2);
        tx = 16 + badge + 14;
      }
      ctx.textAlign = 'left';
      ctx.font = f;
      ctx.fillStyle = solid ? T.onAccent : c.text;
      ctx.fillText(text, tx, h / 2 + 2);
      return cv;
    }

    function leader(view, from, to, color) {
      var g = geo(new THREE.BufferGeometry());
      g.setAttribute('position', new THREE.Float32BufferAttribute([from.x, from.y, from.z, to.x, to.y, to.z], 3));
      var line = new THREE.Line(g, lineMat(color, 0.9));
      view.group.add(line);
      var dot = new THREE.Mesh(geo(new THREE.SphereGeometry(0.012, 12, 8)), basic(color, 1));
      dot.position.copy(to);
      view.group.add(dot);
      return line;
    }

    function makeLabel(view, pl) {
      var el = view.el, style = el.style || 'accent';
      var c = styleColors(T, style);
      var cv = drawPill(el.text || '', style);
      var mesh = canvasPlane(cv, { depthTest: false });
      mesh.renderOrder = 20;
      mesh.userData.billboard = true;
      mesh.scale.setScalar(view.scale);
      view.group.add(mesh);
      view.pill = mesh;
      view.pillHeight = cv.h * MM * view.scale;
      if (pl.pivotBottom) {
        view.stack = { kind: 'label', height: view.pillHeight, rise: 0.14 * UI, current: 0.14 * UI };
        var line = leader(view, new THREE.Vector3(0, 0.14 * UI, 0), new THREE.Vector3(0, 0, 0), c.main);
        view.updaters.push(function () {
          mesh.position.y = view.stack.current;
          var a = line.geometry.attributes.position;
          a.setY(0, Math.max(0, view.stack.current - view.pillHeight / 2));
          a.needsUpdate = true;
        });
      }
      mesh.userData.onClick = function () { emit({ type: 'click', buildId: view.build.id, elementId: el.id, elementType: el.type }); };
    }

    // -- pin ------------------------------------------------------------------------------------

    function makePin(view, pl) {
      var el = view.el, style = el.style || 'accent', c = styleColors(T, style);
      var d = 84, cv = newCanvas(d + 8, d + 8), ctx = cv.ctx;
      ctx.translate(4, 4);
      ctx.beginPath(); ctx.arc(d / 2, d / 2, d / 2 - 2, 0, Math.PI * 2);
      ctx.fillStyle = c.main; ctx.fill();
      ctx.lineWidth = 5; ctx.strokeStyle = T.panel; ctx.stroke();
      ctx.fillStyle = '#FFFFFF'; ctx.textAlign = 'center';
      var label = el.text ? el.text : String(el.number || '');
      ctx.font = font(700, label.length > 1 ? 38 : 48);
      ctx.fillText(label, d / 2, d / 2 + 3);
      var mesh = canvasPlane(cv, { depthTest: false });
      mesh.renderOrder = 21;
      mesh.userData.billboard = true;
      mesh.scale.setScalar(view.scale);
      view.group.add(mesh);
      view.pinDisc = mesh;
      var h = cv.h * MM * view.scale;
      view.stack = { kind: 'pin', height: h, rise: 0.15 * UI, current: 0.15 * UI };
      var line = leader(view, new THREE.Vector3(0, 0.15 * UI, 0), new THREE.Vector3(0, 0, 0), c.main);
      view.updaters.push(function () {
        var active = view.build.stepCount > 0 && el.number === view.build.currentStep;
        var target = view.scale * (active ? 1.18 : 1);
        mesh.scale.setScalar(mesh.scale.x + (target - mesh.scale.x) * 0.2);
        mesh.position.y = view.stack.current;
        var a = line.geometry.attributes.position;
        a.setY(0, Math.max(0, view.stack.current - h / 2));
        a.needsUpdate = true;
      });
      mesh.userData.onClick = function () {
        emit({ type: 'click', buildId: view.build.id, elementId: el.id, elementType: el.type });
        if (view.build.stepCount > 0 && el.number > 0) api.setStep(view.build.id, el.number);
      };
    }

    var lookDir = new THREE.Vector3();
    /** Heading that turns a +Z-facing plane square to the screen (parallel to the image plane, not aimed at the eye point). */
    function cameraYaw() {
      camera.getWorldDirection(lookDir);
      return Math.atan2(-lookDir.x, -lookDir.z);
    }

    /**
     * In a headset, panels face the user. Here the camera is the viewer, so panels keep the tilt the solver gave
     * them but turn about the vertical axis to face the camera. Wall-mounted content stays flat on its wall.
     */
    function facing(view, holder, pl) {
      var base = quat(pl.rotation);
      holder.quaternion.copy(base);
      if (!faceCamera || (view.el.anchor && view.el.anchor.at === 'wall')) return;
      var e = new THREE.Euler().setFromQuaternion(base, 'YXZ');
      view.updaters.push(function () {
        holder.rotation.set(e.x, cameraYaw(), 0, 'YXZ');
      });
    }

    // -- card / panel ---------------------------------------------------------------------------

    function drawCard(view) {
      var el = view.el, b = view.build, c = styleColors(T, el.style || 'accent');
      var isCard = el.type === 'card';
      var W = 600, pad = 34, titleF = font(600, 44), bodyF = font(400, 31), smallF = font(500, 26);
      var numD = isCard && el.number ? 64 : 0;
      var titleX = pad + (numD ? numD + 20 : 0);
      var titleLines = wrap(el.title || '', titleF, W - titleX - pad);
      var bodyLines = wrap(el.body || '', bodyF, W - pad * 2);
      var titleH = Math.max(numD, titleLines.length * 52);
      var bodyH = bodyLines.length ? bodyLines.length * 42 + 18 : 0;
      var stepped = isCard && b.stepCount > 0 && el.step > 0;
      var footH = stepped ? 78 : 0;
      var H = pad + titleH + bodyH + footH + pad - (stepped ? 8 : 0);
      var cv = newCanvas(W + 8, H + 8), ctx = cv.ctx;
      ctx.translate(4, 4);
      roundRect(ctx, 0, 0, W, H, 30);
      ctx.fillStyle = T.panel; ctx.fill();
      ctx.lineWidth = 4; ctx.strokeStyle = c.main; ctx.stroke();

      var y = pad;
      if (numD) {
        ctx.beginPath(); ctx.arc(pad + numD / 2, y + titleH / 2, numD / 2 - 2, 0, Math.PI * 2);
        ctx.lineWidth = 4; ctx.strokeStyle = c.main; ctx.stroke();
        ctx.fillStyle = c.text; ctx.font = font(700, 36); ctx.textAlign = 'center';
        ctx.fillText(String(el.number), pad + numD / 2, y + titleH / 2 + 2);
      }
      ctx.textAlign = 'left'; ctx.font = titleF; ctx.fillStyle = isCard ? c.text : T.ink;
      var ty = y + (titleH - titleLines.length * 52) / 2 + 26;
      titleLines.forEach(function (ln, i) { ctx.fillText(ln, titleX, ty + i * 52 + 2); });
      y += titleH + 18;
      ctx.font = bodyF; ctx.fillStyle = T.muted;
      bodyLines.forEach(function (ln, i) { ctx.fillText(ln, pad, y + i * 42 + 20); });
      y += bodyLines.length * 42;

      var regions = [];
      if (stepped) {
        var fy = H - pad - 56, bh = 56;
        ctx.font = smallF; ctx.fillStyle = T.muted; ctx.textAlign = 'left';
        ctx.fillText(el.step + ' of ' + b.stepCount, pad, fy + bh / 2 + 2);
        var last = el.step >= b.stepCount, first = el.step <= 1;
        var nextW = 132, backW = 118, gap = 12;
        var nx = W - pad - nextW, bx = nx - gap - backW;
        // Back
        roundRect(ctx, bx, fy, backW, bh, bh / 2);
        ctx.fillStyle = T.panelAlt; ctx.globalAlpha = first ? 0.45 : 1; ctx.fill();
        ctx.fillStyle = T.ink; ctx.font = font(600, 27); ctx.textAlign = 'center';
        ctx.fillText('Back', bx + backW / 2, fy + bh / 2 + 2);
        ctx.globalAlpha = 1;
        // Next / Done
        roundRect(ctx, nx, fy, nextW, bh, bh / 2);
        ctx.fillStyle = themeName === 'dark' ? T.accent : T.ink; ctx.fill();
        ctx.fillStyle = '#FFFFFF';
        ctx.fillText(last ? 'Done' : 'Next', nx + nextW / 2, fy + bh / 2 + 2);
        regions.push({ x: bx, y: fy, w: backW, h: bh, action: first ? null : 'back' });
        regions.push({ x: nx, y: fy, w: nextW, h: bh, action: last ? 'done' : 'next' });
      }
      cv.regions = regions; cv.W = W; cv.H = H;
      return cv;
    }

    function makeCard(view, pl) {
      var el = view.el;
      var cv = drawCard(view);
      // Drawn over the twin: at docs scale a card is larger than life and would otherwise cut into the equipment.
      var mesh = canvasPlane(cv, { depthTest: false });
      mesh.renderOrder = 25; // above labels and pins: the card is what the user is reading
      var holder = new THREE.Group();
      facing(view, holder, pl);
      holder.scale.setScalar(view.scale);
      if (pl.pivotBottom) mesh.position.y = cv.h * MM / 2;
      else mesh.position.y = -cv.h * MM * 0.3; // hang below the callout point so the part itself stays visible
      holder.add(mesh);
      view.group.add(holder);
      mesh.userData.onClick = function (uv) {
        var px = uv.x * (cv.w) - 4, py = (1 - uv.y) * (cv.h) - 4, hit = null;
        cv.regions.forEach(function (r) { if (px >= r.x && px <= r.x + r.w && py >= r.y && py <= r.y + r.h) hit = r; });
        emit({ type: 'click', buildId: view.build.id, elementId: el.id, elementType: el.type, action: hit ? hit.action : null });
        if (!hit || !hit.action) return;
        if (hit.action === 'next') api.setStep(view.build.id, view.build.currentStep + 1);
        else if (hit.action === 'back') api.setStep(view.build.id, view.build.currentStep - 1);
      };
    }

    // -- checklist ------------------------------------------------------------------------------

    function drawChecklist(view) {
      var el = view.el, c = styleColors(T, el.style || 'accent');
      var W = 440, pad = 34, rowH = 58, titleH = 62;
      var people = (el.people || []).slice(), more = el.more || 0;
      var avatarsH = (people.length || more) ? 76 : 0;
      var H = pad + titleH + el.items.length * rowH + avatarsH + pad - 6;
      var cv = newCanvas(W + 8, H + 8), ctx = cv.ctx;
      ctx.translate(4, 4);
      roundRect(ctx, 0, 0, W, H, 30);
      ctx.fillStyle = T.panel; ctx.fill();
      ctx.lineWidth = 4; ctx.strokeStyle = c.main; ctx.stroke();
      ctx.textAlign = 'left'; ctx.font = font(700, 40); ctx.fillStyle = T.ink;
      ctx.fillText(el.title || 'Checklist', pad, pad + 26);
      var regions = [], y = pad + titleH;
      el.items.forEach(function (item, i) {
        var bx = pad, by = y + (rowH - 36) / 2;
        roundRect(ctx, bx, by, 36, 36, 9);
        if (item.done) {
          ctx.fillStyle = c.main; ctx.fill();
          ctx.strokeStyle = '#FFFFFF'; ctx.lineWidth = 5; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
          ctx.beginPath(); ctx.moveTo(bx + 9, by + 19); ctx.lineTo(bx + 16, by + 26); ctx.lineTo(bx + 28, by + 11); ctx.stroke();
        } else {
          ctx.lineWidth = 3.5; ctx.strokeStyle = T.muted; ctx.globalAlpha = 0.7; ctx.stroke(); ctx.globalAlpha = 1;
        }
        ctx.font = font(500, 33); ctx.fillStyle = item.done ? T.muted : T.ink;
        ctx.fillText(item.text, bx + 54, y + rowH / 2 + 2);
        regions.push({ x: 0, y: y, w: W, h: rowH, index: i });
        y += rowH;
      });
      if (avatarsH) {
        var ax = pad, ay = y + 14, d = 50;
        people.concat(more ? ['+' + more] : []).forEach(function (p, i) {
          var isMore = more && i === people.length;
          ctx.beginPath(); ctx.arc(ax + d / 2, ay + d / 2, d / 2, 0, Math.PI * 2);
          ctx.fillStyle = isMore ? c.soft : c.main; ctx.fill();
          ctx.lineWidth = 4; ctx.strokeStyle = T.panel; ctx.stroke();
          ctx.fillStyle = isMore ? c.text : '#FFFFFF'; ctx.font = font(700, isMore ? 22 : 25); ctx.textAlign = 'center';
          ctx.fillText(p, ax + d / 2, ay + d / 2 + 2);
          ax += d - 10;
        });
      }
      cv.regions = regions;
      return cv;
    }

    function makeChecklist(view, pl) {
      var el = view.el;
      var holder = new THREE.Group();
      facing(view, holder, pl);
      holder.scale.setScalar(view.scale);
      view.group.add(holder);
      var cv = drawChecklist(view);
      var mesh = canvasPlane(cv, { depthTest: false });
      mesh.renderOrder = 25; // above labels and pins: the card is what the user is reading
      if (pl.pivotBottom) mesh.position.y = cv.h * MM / 2;
      holder.add(mesh);
      mesh.userData.onClick = function (uv) {
        var c2 = mesh.userData.cv;
        var px = uv.x * c2.w - 4, py = (1 - uv.y) * c2.h - 4, hit = null;
        c2.regions.forEach(function (r) { if (px >= r.x && px <= r.x + r.w && py >= r.y && py <= r.y + r.h) hit = r; });
        if (!hit) { emit({ type: 'click', buildId: view.build.id, elementId: el.id, elementType: el.type }); return; }
        api.toggleChecklistItem(view.build.id, el.id, hit.index);
      };
      view.redraw = function () {
        var next = drawChecklist(view);
        release(mesh.material.map);
        mesh.material.map = texture(next.canvas);
        mesh.material.needsUpdate = true;
        mesh.userData.cv = next;
      };
    }

    // -- gizmo ----------------------------------------------------------------------------------

    function makeGizmo(view, pl) {
      var el = view.el, c = styleColors(T, el.style || 'accent');
      var holder = new THREE.Group();
      holder.quaternion.copy(quat(pl.rotation));
      holder.scale.setScalar(view.scale);
      holder.position.y = -0.045; // wrap the handwheel, which sits just below the top of the part's bounds
      view.group.add(holder);
      var spin = new THREE.Group();
      holder.add(spin);
      var R = 0.15, tube = 0.016, arc = Math.PI * 1.55;
      var cw = el.action !== 'turn-ccw';
      var mat = basic(c.main, 0.95);
      var torus = new THREE.Mesh(geo(new THREE.TorusGeometry(R, tube, 10, 48, arc)), mat);
      torus.rotation.x = Math.PI / 2; // into the XZ plane; arc now runs from +X toward +Z
      spin.add(torus);
      // After the rotation the arc ends at angle `arc` measured from +X toward +Z (three), which is clockwise seen from above.
      var endA = cw ? arc : 0;
      var head = new THREE.Mesh(geo(new THREE.ConeGeometry(tube * 3.1, tube * 6.5, 16)), mat);
      head.position.set(Math.cos(endA) * R, 0, Math.sin(endA) * R);
      // tangent direction of travel
      var dir = cw ? new THREE.Vector3(-Math.sin(endA), 0, Math.cos(endA)) : new THREE.Vector3(Math.sin(endA), 0, -Math.cos(endA));
      head.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
      head.position.addScaledVector(dir, tube * 2.2);
      spin.add(head);
      var glow = new THREE.Mesh(geo(new THREE.RingGeometry(R - 0.035, R + 0.035, 48)), basic(c.main, 0.12, { side: THREE.DoubleSide, depthWrite: false }));
      glow.rotation.x = -Math.PI / 2;
      holder.add(glow);
      view.updaters.push(function (dt) { spin.rotation.y += (cw ? -1 : 1) * dt * 1.9; });
    }

    // -- highlight ------------------------------------------------------------------------------

    function volumeFor(entity, inflate) {
      var s = entity.size, axis = 0;
      if (s[1] > s[axis]) axis = 1;
      if (s[2] > s[axis]) axis = 2;
      var others = [0, 1, 2].filter(function (i) { return i !== axis; });
      var a = s[others[0]], b = s[others[1]];
      var round = Math.min(a, b) / Math.max(a, b) > 0.62;
      if (!round) return { box: true, size: [s[0] * inflate, s[1] * inflate, s[2] * inflate + 0.02] };
      return { box: false, axis: axis, radius: (a + b) / 4 * inflate, length: s[axis] * 1.02 };
    }

    function volumeMesh(vol, material, radiusOverride) {
      var mesh;
      if (vol.box) mesh = new THREE.Mesh(geo(new THREE.BoxGeometry(vol.size[0], vol.size[1], vol.size[2])), material);
      else {
        mesh = new THREE.Mesh(geo(new THREE.CylinderGeometry(radiusOverride || vol.radius, radiusOverride || vol.radius, vol.length, 40, 1, false)), material);
        if (vol.axis === 0) mesh.rotation.z = Math.PI / 2;
        if (vol.axis === 2) mesh.rotation.x = Math.PI / 2;
      }
      return mesh;
    }

    function makeHighlight(view, pl) {
      var el = view.el, c = styleColors(T, el.style || 'accent');
      var entity = entityById[el.anchor.entity];
      if (!entity) return;
      var holder = new THREE.Group();
      holder.quaternion.copy(quat(pl.rotation));
      holder.scale.setScalar(view.scale);
      view.group.add(holder);
      var vol = volumeFor(entity, 1.08);
      var fill = basic(c.main, 0.26, { depthWrite: false, side: THREE.DoubleSide });
      var mesh = volumeMesh(vol, fill);
      mesh.renderOrder = 5;
      holder.add(mesh);
      var edges = new THREE.LineSegments(geo(new THREE.EdgesGeometry(mesh.geometry, 30)), lineMat(c.main, 0.9));
      edges.rotation.copy(mesh.rotation);
      holder.add(edges);
      view.updaters.push(function (dt, time) {
        fill.userData.pulse = 0.72 + 0.28 * Math.sin(time * 3.2);
      });
      view.pulsing = true;
    }

    // -- flow -----------------------------------------------------------------------------------

    function chevronTexture(color) {
      var c = document.createElement('canvas');
      c.width = 128; c.height = 64;
      var ctx = c.getContext('2d');
      ctx.clearRect(0, 0, 128, 64);
      ctx.fillStyle = color;
      ctx.globalAlpha = 0.28; ctx.fillRect(0, 0, 128, 64);
      ctx.globalAlpha = 1;
      // Chevron pointing toward +U; V wraps around the tube so it is drawn twice for symmetry.
      for (var k = 0; k < 2; k++) {
        var oy = k * 32;
        ctx.beginPath();
        ctx.moveTo(30, oy + 2); ctx.lineTo(70, oy + 2); ctx.lineTo(98, oy + 16); ctx.lineTo(70, oy + 30); ctx.lineTo(30, oy + 30); ctx.lineTo(58, oy + 16);
        ctx.closePath(); ctx.fill();
      }
      var t = new THREE.CanvasTexture(c);
      t.wrapS = t.wrapT = THREE.RepeatWrapping;
      t.anisotropy = maxAniso;
      return track(t);
    }

    function makeFlow(view, pl) {
      var el = view.el, c = styleColors(T, el.style || 'accent');
      view.noScale = true;
      // The path is in world space, so undo the group's translation.
      var world = new THREE.Group();
      world.position.copy(view.group.position).multiplyScalar(-1);
      view.group.add(world);
      var pts = (pl.path || []).map(v3);
      var radius = (el.radius || 0.07) * 1.16;
      var texs = [], travelled = 0, period = 0.22;
      for (var i = 0; i < pts.length - 1; i++) {
        var a = pts[i], b = pts[i + 1], len = a.distanceTo(b);
        if (len < 1e-4) continue;
        var tex = chevronTexture(c.main);
        // CylinderGeometry maps U around and V along the axis; rotate the texture so chevrons run along the pipe.
        tex.rotation = Math.PI / 2; tex.center.set(0, 0);
        tex.repeat.set(len / period, 2);
        tex.userData = { start: travelled / period };
        texs.push(tex);
        var m = track(new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false, side: THREE.DoubleSide, opacity: 0.95 }));
        m.userData.base = 0.95;
        var seg = new THREE.Mesh(geo(new THREE.CylinderGeometry(radius, radius, len, 28, 1, true)), m);
        seg.position.copy(a).add(b).multiplyScalar(0.5);
        seg.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), b.clone().sub(a).normalize());
        seg.renderOrder = 6;
        world.add(seg);
        if (i > 0) {
          var joint = new THREE.Mesh(geo(new THREE.SphereGeometry(radius, 20, 14)), basic(c.main, 0.3, { depthWrite: false }));
          joint.position.copy(a);
          world.add(joint);
        }
        travelled += len;
      }
      // Arrow head at the downstream end.
      if (pts.length >= 2) {
        var endDir = pts[pts.length - 1].clone().sub(pts[pts.length - 2]).normalize();
        var tip = new THREE.Mesh(geo(new THREE.ConeGeometry(radius * 1.25, radius * 1.9, 24)), basic(c.main, 0.9));
        tip.position.copy(pts[pts.length - 1]).addScaledVector(endDir, radius * 0.95);
        tip.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), endDir);
        world.add(tip);
      }
      // Lit sections of pipe.
      (el.entities || []).forEach(function (id) {
        var e = entityById[id];
        if (!e) return;
        var vol = volumeFor(e, 1.0);
        var g = new THREE.Group();
        g.position.copy(v3(e.position));
        g.quaternion.copy(quat(e.rotation));
        var glow = volumeMesh(vol, basic(c.main, 0.16, { depthWrite: false, side: THREE.DoubleSide }), vol.box ? null : (el.radius || 0.07) * 1.55);
        glow.renderOrder = 4;
        g.add(glow);
        world.add(g);
      });
      // Flow rate tag near the corner of the path.
      if (el.text && pts.length >= 2) {
        var cv = drawPill(el.text, 'neutral');
        var tag = canvasPlane(cv, { depthTest: false });
        tag.userData.billboard = true;
        tag.renderOrder = 19;
        tag.scale.setScalar(0.62);
        var mid = pts[0].clone().lerp(pts[1], 0.42);
        tag.position.copy(mid).add(new THREE.Vector3(0, -0.2, 0.22));
        world.add(tag);
      }
      var offset = 0;
      view.updaters.push(function (dt) {
        offset += dt * 1.6;
        for (var k = 0; k < texs.length; k++) {
          // With the 90 degree texture rotation, "along the pipe" is the texture's U after rotation.
          texs[k].offset.set(((texs[k].userData.start - offset) % 1) - 1, 0);
        }
      });
    }

    // -- chart ----------------------------------------------------------------------------------

    function makeChart(view, pl) {
      var el = view.el, c = styleColors(T, el.style || 'accent');
      var holder = new THREE.Group();
      holder.quaternion.copy(quat(pl.rotation));
      if (faceCamera && !(el.anchor && el.anchor.at === 'wall')) {
        // A chart is placed facing whoever asked for it. Here that is the camera, at the moment it is built.
        holder.rotation.set(0, cameraYaw(), 0);
      }
      holder.scale.setScalar(view.scale);
      view.group.add(holder);
      var values = el.values || [], n = values.length;
      var width = Math.max(0.4, n * 0.085 + 0.12), depth = 0.3, slabH = 0.022;
      var slab = new THREE.Mesh(geo(new THREE.BoxGeometry(width, slabH, depth)), basic(T.panel, 1, { transparent: true }));
      slab.position.y = slabH / 2;
      holder.add(slab);
      var slabEdges = new THREE.LineSegments(geo(new THREE.EdgesGeometry(slab.geometry)), lineMat(c.main, 1));
      slabEdges.position.copy(slab.position);
      holder.add(slabEdges);

      var max = 0;
      values.forEach(function (v) { if (v > max) max = v; });
      if (max <= 0) max = 1;
      var maxH = 0.52, barW = 0.058, barD = 0.07, x0 = -((n - 1) * 0.085) / 2;
      var bars = [], linePts = [];
      var barMat = basic(c.main, 0.82);
      var capMat = basic(themeName === 'dark' ? '#B9B1FA' : '#A79DF2', 0.95);
      var barEdge = lineMat(themeName === 'dark' ? '#C9C3FB' : '#5546CF', 0.55);
      values.forEach(function (v, i) {
        var h = Math.max(0.01, v / max * maxH);
        var g = new THREE.Group();
        g.position.set(x0 + i * 0.085, slabH, -0.01);
        var box = geo(new THREE.BoxGeometry(barW, 1, barD));
        box.translate(0, 0.5, 0);
        var bar = new THREE.Mesh(box, barMat);
        var cap = new THREE.Mesh(geo(new THREE.PlaneGeometry(barW, barD)), capMat);
        cap.rotation.x = -Math.PI / 2; cap.position.y = 1.001;
        var edges = new THREE.LineSegments(geo(new THREE.EdgesGeometry(box)), barEdge);
        g.add(bar, cap, edges);
        g.scale.y = 0.001;
        holder.add(g);
        bars.push({ g: g, h: h });
        linePts.push(new THREE.Vector3(x0 + i * 0.085, slabH + h + 0.045, -0.01));
      });
      // Trend line with dots, revealed after the bars have grown.
      var trend = new THREE.Group();
      if (linePts.length > 1) {
        var lg = geo(new THREE.BufferGeometry().setFromPoints(linePts));
        trend.add(new THREE.Line(lg, lineMat(themeName === 'dark' ? '#D6D1FC' : '#3F33A8', 1)));
        linePts.forEach(function (p, i) {
          var last = i === linePts.length - 1;
          var dot = new THREE.Mesh(geo(new THREE.SphereGeometry(last ? 0.014 : 0.008, 12, 8)), basic(themeName === 'dark' ? '#D6D1FC' : '#3F33A8', 1));
          dot.position.copy(p);
          trend.add(dot);
        });
      }
      holder.add(trend);

      // Title lying on the front edge of the slab, tilted up toward the viewer.
      var title = el.title || '';
      var tf = font(600, 40), uf = font(500, 28);
      var tw = Math.ceil(measure(title, tf)), uw = el.unit ? Math.ceil(measure(el.unit, uf)) + 22 : 0;
      var cv = newCanvas(tw + uw + 56, 72), ctx = cv.ctx;
      roundRect(ctx, 2, 2, cv.w - 4, cv.h - 4, 22);
      ctx.fillStyle = T.panel; ctx.fill(); ctx.lineWidth = 3.5; ctx.strokeStyle = c.main; ctx.stroke();
      ctx.font = tf; ctx.fillStyle = T.ink; ctx.textAlign = 'left';
      ctx.fillText(title, 28, cv.h / 2 + 2);
      if (el.unit) { ctx.font = uf; ctx.fillStyle = T.muted; ctx.fillText(el.unit, 28 + tw + 22, cv.h / 2 + 3); }
      var plate = canvasPlane(cv);
      var k = Math.min(1, (width - 0.02) / (cv.w * MM));
      plate.scale.setScalar(k);
      plate.rotation.x = -Math.PI / 3.2;
      plate.position.set(0, 0.03, depth / 2 + cv.h * MM * k * 0.36);
      plate.renderOrder = 9;
      holder.add(plate);

      // Value of the latest bar.
      if (n > 0) {
        var vcv = drawPill(String(values[n - 1]), 'accent');
        var tag = canvasPlane(vcv, { depthTest: false });
        tag.userData.billboard = true;
        tag.scale.setScalar(0.5);
        tag.renderOrder = 19;
        tag.position.copy(linePts[n - 1]).add(new THREE.Vector3(0, 0.07, 0));
        holder.add(tag);
        trend.add(new THREE.Object3D());
        view.valueTag = tag;
      }

      var age = 0;
      view.updaters.push(function (dt) {
        age += dt;
        for (var i = 0; i < bars.length; i++) {
          var t = clamp((age - 0.12 - i * 0.045) / 0.45, 0, 1);
          bars[i].g.scale.y = Math.max(0.001, bars[i].h * easeOut(t));
        }
        var done = clamp((age - 0.12 - bars.length * 0.045 - 0.25) / 0.3, 0, 1);
        trend.visible = done > 0;
        if (view.valueTag) view.valueTag.visible = done > 0.5;
      });
    }

    // -- avatar ---------------------------------------------------------------------------------

    function makeAvatar(view, pl) {
      var el = view.el, c = styleColors(T, el.style || 'accent');
      var holder = new THREE.Group();
      holder.scale.setScalar(view.scale);
      view.group.add(holder);
      var ring = new THREE.Mesh(geo(new THREE.RingGeometry(0.15, 0.2, 40)), basic(c.main, 0.35, { side: THREE.DoubleSide, depthWrite: false }));
      ring.rotation.x = -Math.PI / 2; ring.position.y = 0.004;
      var disc = new THREE.Mesh(geo(new THREE.CircleGeometry(0.15, 40)), basic(c.main, 0.16, { side: THREE.DoubleSide, depthWrite: false }));
      disc.rotation.x = -Math.PI / 2; disc.position.y = 0.003;
      holder.add(ring, disc);
      var bob = new THREE.Group();
      holder.add(bob);
      var cone = new THREE.Mesh(geo(new THREE.CylinderGeometry(0.035, 0.13, 0.46, 32)), basic(c.main, 0.88));
      cone.position.y = 0.25;
      bob.add(cone);
      var coneEdge = new THREE.LineSegments(geo(new THREE.EdgesGeometry(cone.geometry, 40)), lineMat(themeName === 'dark' ? '#C9C3FB' : '#4A3CC0', 0.6));
      coneEdge.position.copy(cone.position);
      bob.add(coneEdge);
      // Head disc with the initial.
      var d = 110, cv = newCanvas(d + 8, d + 8), ctx = cv.ctx;
      ctx.translate(4, 4);
      ctx.beginPath(); ctx.arc(d / 2, d / 2, d / 2 - 2, 0, Math.PI * 2);
      ctx.fillStyle = c.main; ctx.fill(); ctx.lineWidth = 5; ctx.strokeStyle = T.panel; ctx.stroke();
      ctx.fillStyle = '#FFFFFF'; ctx.font = font(700, 56); ctx.textAlign = 'center';
      ctx.fillText(el.text || (el.name || '?').charAt(0), d / 2, d / 2 + 3);
      var head = canvasPlane(cv, { depthTest: false });
      head.userData.billboard = true; head.renderOrder = 21;
      head.position.y = 0.5 + 0.06 * UI;
      bob.add(head);
      var pill = canvasPlane(drawPill(el.name || 'Guest', 'neutral'), { depthTest: false });
      pill.userData.billboard = true; pill.renderOrder = 21;
      pill.scale.setScalar(0.9);
      pill.position.y = 0.5 + 0.2 * UI;
      bob.add(pill);
      head.userData.onClick = pill.userData.onClick = function () { emit({ type: 'click', buildId: view.build.id, elementId: el.id, elementType: el.type }); };
      view.updaters.push(function (dt, time) {
        bob.position.y = 0.012 * Math.sin(time * 2.1);
        ring.scale.setScalar(1 + 0.06 * Math.sin(time * 2.1));
      });
    }

    var MAKERS = {
      label: makeLabel, pin: makePin, card: makeCard, panel: makeCard, checklist: makeChecklist,
      gizmo: makeGizmo, highlight: makeHighlight, flow: makeFlow, chart: makeChart, avatar: makeAvatar
    };

    // ---- build store (same semantics as BuildStore.Apply) -------------------------------------

    function findBuild(id) {
      for (var i = 0; i < builds.length; i++) if (builds[i].id === id) return builds[i];
      return null;
    }

    function isVisible(build, el) { return !el.step || el.step === build.currentStep; }

    function addBuild(json, placements) {
      removeBuild(json.id);
      var b = {
        id: json.id, kind: json.kind || 'custom', title: json.title || '', stepCount: json.stepCount || 0,
        currentStep: json.currentStep || 0, elements: JSON.parse(JSON.stringify(json.elements || [])),
        placements: placements || {}, views: []
      };
      b.elements.forEach(function (el) {
        var view = makeView(b, el);
        view.wanted = isVisible(b, el);
        b.views.push(view);
      });
      builds.push(b);
      restack();
    }

    function removeBuild(id) {
      var b = findBuild(id);
      if (!b) return false;
      builds.splice(builds.indexOf(b), 1);
      b.views.forEach(function (v) { v.wanted = false; v.dead = true; dying.push(v); });
      restack();
      return true;
    }

    function refreshStepped(b) {
      // Each step has its own card, gizmo and focus volume; moving the step only changes which ones are wanted.
      b.views.forEach(function (v) { v.wanted = isVisible(b, v.el); });
      restack();
    }

    /** Pins and labels that share an anchor point stack upward instead of overlapping. */
    function restack() {
      var groups = {};
      builds.forEach(function (b) {
        b.views.forEach(function (v) {
          if (!v.stack || !v.wanted) return;
          var t = v.placement.target || v.placement.position;
          var key = Math.round(t[0] * 20) + '/' + Math.round(t[1] * 20) + '/' + Math.round(t[2] * 20);
          (groups[key] = groups[key] || []).push(v);
        });
      });
      Object.keys(groups).forEach(function (k) {
        var list = groups[k];
        list.sort(function (a, b2) { return (a.stack.kind === 'pin' ? 0 : 1) - (b2.stack.kind === 'pin' ? 0 : 1); });
        var cursor = 0.07 * UI;
        list.forEach(function (v) {
          v.stack.rise = cursor + v.stack.height / 2;
          cursor += v.stack.height + 0.014 * UI;
        });
      });
    }

    // ---- camera + controls --------------------------------------------------------------------

    var HOME = { target: new THREE.Vector3(-0.42, 0.62, -0.1), theta: -0.5, phi: 1.1, radius: 5.9 };
    var orbit = { target: HOME.target.clone(), theta: HOME.theta, phi: HOME.phi, radius: HOME.radius };
    var fit = 1;         // extra distance for narrow viewports
    var tween = null;

    function applyCamera() {
      var r = orbit.radius * fit, sp = Math.sin(orbit.phi);
      camera.position.set(
        orbit.target.x + r * sp * Math.sin(orbit.theta),
        orbit.target.y + r * Math.cos(orbit.phi),
        orbit.target.z + r * sp * Math.cos(orbit.theta));
      camera.lookAt(orbit.target);
    }

    function flyTo(target, radius, theta, phi) {
      tween = {
        t: 0, from: { target: orbit.target.clone(), radius: orbit.radius, theta: orbit.theta, phi: orbit.phi },
        to: { target: target, radius: radius, theta: theta == null ? orbit.theta : theta, phi: phi == null ? orbit.phi : phi }
      };
    }

    var pointers = {}, pointerCount = 0, engaged = false, downAt = null, lastPinch = 0, lastMid = null;

    function onPointerDown(e) {
      engaged = true;
      pointers[e.pointerId] = { x: e.clientX, y: e.clientY, button: e.button, shift: e.shiftKey };
      pointerCount = Object.keys(pointers).length;
      if (pointerCount === 1) downAt = { x: e.clientX, y: e.clientY, t: performance.now() };
      else downAt = null;
      lastPinch = 0; lastMid = null;
      try { canvas.setPointerCapture(e.pointerId); } catch (err) { /* not capturable */ }
      tween = null;
    }

    function pan(dx, dy) {
      var scale = orbit.radius * fit * Math.tan(camera.fov * Math.PI / 360) * 2 / Math.max(1, canvas.clientHeight);
      var right = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 0);
      var up = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 1);
      orbit.target.addScaledVector(right, -dx * scale).addScaledVector(up, dy * scale);
      orbit.target.y = clamp(orbit.target.y, 0, 2.4);
      orbit.target.x = clamp(orbit.target.x, -4, 4);
      orbit.target.z = clamp(orbit.target.z, -4, 5);
    }

    function onPointerMove(e) {
      var p = pointers[e.pointerId];
      if (!p) { hover(e); return; }
      var dx = e.clientX - p.x, dy = e.clientY - p.y;
      p.x = e.clientX; p.y = e.clientY;
      if (pointerCount === 1) {
        if (p.button === 2 || p.button === 1 || p.shift) pan(dx, dy);
        else {
          orbit.theta -= dx * 0.006;
          orbit.phi = clamp(orbit.phi - dy * 0.005, 0.18, 1.5);
        }
      } else if (pointerCount === 2) {
        var ids = Object.keys(pointers), a = pointers[ids[0]], b = pointers[ids[1]];
        var dist = Math.hypot(a.x - b.x, a.y - b.y), mid = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
        if (lastPinch > 0) orbit.radius = clamp(orbit.radius * lastPinch / Math.max(1, dist), 1.6, 16);
        if (lastMid) pan(mid.x - lastMid.x, mid.y - lastMid.y);
        lastPinch = dist; lastMid = mid;
      }
    }

    function onPointerUp(e) {
      var p = pointers[e.pointerId];
      delete pointers[e.pointerId];
      pointerCount = Object.keys(pointers).length;
      lastPinch = 0; lastMid = null;
      if (p && downAt && e.type === 'pointerup' && Math.hypot(e.clientX - downAt.x, e.clientY - downAt.y) < 6 && performance.now() - downAt.t < 600) click(e);
      downAt = null;
    }

    function onWheel(e) {
      // Plain wheel scrolls the page unless the user has already grabbed the view; pinch (ctrl+wheel) always zooms.
      if (!engaged && !e.ctrlKey) return;
      e.preventDefault();
      var k = Math.exp((e.ctrlKey ? e.deltaY * 0.012 : e.deltaY * 0.0012));
      orbit.radius = clamp(orbit.radius * k, 1.6, 16);
      tween = null;
    }

    var raycaster = new THREE.Raycaster(), ndc = new THREE.Vector2();

    function pick(e) {
      var rect = canvas.getBoundingClientRect();
      ndc.set(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1);
      raycaster.setFromCamera(ndc, camera);
      var live = clickables.filter(function (o) { return o.userData.view && o.userData.view.wanted && o.userData.view.appear > 0.5; });
      var hits = raycaster.intersectObjects(live, false);
      // Depth-test-off billboards draw on top, so prefer them over what is geometrically nearer.
      hits.sort(function (a, b) { return (b.object.renderOrder - a.object.renderOrder) || (a.distance - b.distance); });
      return hits.length ? hits[0] : null;
    }

    function click(e) {
      var hit = pick(e);
      if (hit && hit.object.userData.onClick) hit.object.userData.onClick(hit.uv || new THREE.Vector2(0.5, 0.5));
    }

    function hover(e) {
      if (e.pointerType === 'touch') return;
      canvas.style.cursor = pick(e) ? 'pointer' : 'grab';
    }

    function onLeave() { engaged = false; }
    function onContext(e) { e.preventDefault(); }

    canvas.addEventListener('pointerdown', onPointerDown);
    canvas.addEventListener('pointermove', onPointerMove);
    canvas.addEventListener('pointerup', onPointerUp);
    canvas.addEventListener('pointercancel', onPointerUp);
    canvas.addEventListener('pointerleave', onLeave);
    canvas.addEventListener('wheel', onWheel, { passive: false });
    canvas.addEventListener('contextmenu', onContext);

    // ---- frame loop ---------------------------------------------------------------------------

    var visible = true, raf = 0, last = performance.now(), time = 0;
    var tmpV = new THREE.Vector3();

    function frame(now) {
      raf = 0;
      if (disposed) return;
      var dt = Math.min(0.05, (now - last) / 1000);
      last = now;
      time += dt;

      if (tween) {
        tween.t = Math.min(1, tween.t + dt / 0.7);
        var k = easeInOut(tween.t);
        orbit.target.lerpVectors(tween.from.target, tween.to.target, k);
        orbit.radius = tween.from.radius + (tween.to.radius - tween.from.radius) * k;
        orbit.theta = tween.from.theta + (tween.to.theta - tween.from.theta) * k;
        orbit.phi = tween.from.phi + (tween.to.phi - tween.from.phi) * k;
        if (tween.t >= 1) tween = null;
      }
      applyCamera();

      // Walls between the camera and the room would hide everything; fade them out of the way.
      roomGroup.children.forEach(function (g) {
        if (!g.userData.wall) return;
        tmpV.copy(camera.position).sub(g.userData.wall.center);
        g.visible = tmpV.dot(g.userData.wall.normal) > 0;
      });

      var i, v;
      for (i = 0; i < builds.length; i++) {
        for (var j = 0; j < builds[i].views.length; j++) stepView(builds[i].views[j], dt);
      }
      for (i = dying.length - 1; i >= 0; i--) {
        v = dying[i];
        stepView(v, dt);
        if (v.appear <= 0) { removeView(v); dying.splice(i, 1); }
      }
      for (i = 0; i < billboards.length; i++) {
        var o = billboards[i], parentQ = o.parent ? o.parent.getWorldQuaternion(new THREE.Quaternion()) : null;
        o.quaternion.copy(camera.quaternion);
        if (parentQ) o.quaternion.premultiply(parentQ.invert());
      }
      renderer.render(scene, camera);
      schedule();
    }

    function stepView(v, dt) {
      var target = v.wanted ? 1 : 0;
      if (v.appear !== target) v.appear = clamp(v.appear + (target > v.appear ? 1 : -1) * dt / 0.26, 0, 1);
      if (v.stack) v.stack.current += (v.stack.rise - v.stack.current) * Math.min(1, dt * 10);
      if (v.appear > 0) for (var u = 0; u < v.updaters.length; u++) v.updaters[u](dt, time);
      if (v.appear !== v._drawn || v.pulsing) { setViewProgress(v, v.appear); v._drawn = v.appear; }
    }

    function schedule() {
      if (!raf && visible && !disposed) raf = requestAnimationFrame(frame);
    }

    var io = null;
    if (typeof IntersectionObserver !== 'undefined') {
      io = new IntersectionObserver(function (entries) {
        visible = entries[0].isIntersecting;
        if (visible) { last = performance.now(); schedule(); }
      }, { threshold: 0.01 });
      io.observe(container);
    }
    function onVisibility() {
      if (!document.hidden) { last = performance.now(); schedule(); }
    }
    document.addEventListener('visibilitychange', onVisibility);

    function resize() {
      var w = Math.max(1, container.clientWidth), h = Math.max(1, container.clientHeight);
      renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
      // Keep the whole station in frame on tall, narrow viewports.
      fit = Math.max(1, 1.3 / camera.aspect);
      schedule();
    }
    var ro = null;
    if (typeof ResizeObserver !== 'undefined') { ro = new ResizeObserver(resize); ro.observe(container); }
    else window.addEventListener('resize', resize);

    function clearGroup(g) {
      g.traverse(function (o) {
        if (o.geometry) release(o.geometry);
        if (o.material) { if (o.material.map) release(o.material.map); release(o.material); }
      });
      while (g.children.length) g.remove(g.children[0]);
    }

    function applyBackground() {
      if (opts.background === false) return;
      container.style.background = 'linear-gradient(180deg, ' + T.bgTop + ' 0%, ' + T.bgBottom + ' 100%)';
    }

    function rebuildEverything() {
      // Themes change colours baked into canvases and materials, so views are rebuilt in place.
      T = THEMES[themeName];
      hemi.color.setHex(T.hemiSky); hemi.groundColor.setHex(T.hemiGround); hemi.intensity = T.hemi; sun.intensity = T.sun;
      applyBackground();
      buildRoom();
      buildTwin();
      builds.forEach(function (b) {
        b.views.forEach(removeView);
        b.views = b.elements.map(function (el) {
          var v = makeView(b, el);
          v.wanted = isVisible(b, el);
          v.appear = v.wanted ? 1 : 0;
          return v;
        });
      });
      restack();
      builds.forEach(function (b) { b.views.forEach(function (v) { if (v.stack) v.stack.current = v.stack.rise; }); });
    }

    // ---- public API ---------------------------------------------------------------------------

    var api = {
      /** Apply a PlanResult (wire format). `placements` maps "<buildId>/<elementId>" to solver output. */
      applyPlan: function (plan, placements) {
        if (!plan || !plan.ops) return;
        plan.ops.forEach(function (op) {
          if (op.op === 'add' && op.build) addBuild(op.build, placements);
          else if (op.op === 'remove') removeBuild(op.buildId);
          else if (op.op === 'step') api.setStep(op.buildId, op.step);
          else if (op.op === 'clear') builds.slice().forEach(function (b) { removeBuild(b.id); });
        });
        schedule();
      },
      /** Convenience: apply plans[index] from the list given to create(). Returns that entry (or null). */
      applyPlanAt: function (index) {
        var entry = plans[index];
        if (!entry) return null;
        api.applyPlan(entry.plan, entry.placements);
        return entry;
      },
      /** Remove every build and return the camera home. */
      reset: function () {
        builds.slice().forEach(function (b) { removeBuild(b.id); });
        flyTo(HOME.target.clone(), HOME.radius, HOME.theta, HOME.phi);
        schedule();
      },
      builds: function () {
        return builds.map(function (b) {
          return { id: b.id, kind: b.kind, title: b.title, stepCount: b.stepCount, currentStep: b.currentStep, elements: b.elements.length };
        });
      },
      setStep: function (buildId, n) {
        var b = findBuild(buildId);
        if (!b || !b.stepCount) return false;
        var step = clamp(Math.round(n), 1, b.stepCount);
        if (step === b.currentStep) return false;
        b.currentStep = step;
        refreshStepped(b);
        emit({ type: 'step', buildId: b.id, step: step, stepCount: b.stepCount });
        schedule();
        return true;
      },
      toggleChecklistItem: function (buildId, elementId, index) {
        var b = findBuild(buildId);
        if (!b) return false;
        for (var i = 0; i < b.views.length; i++) {
          var v = b.views[i];
          if (v.el.id !== elementId || !v.el.items || !v.el.items[index]) continue;
          v.el.items[index].done = !v.el.items[index].done;
          if (v.redraw) v.redraw();
          emit({ type: 'checklist', buildId: buildId, elementId: elementId, index: index, done: v.el.items[index].done });
          schedule();
          return true;
        }
        return false;
      },
      setTwinMode: function (mode) {
        twinMode = mode === 'ghost' || mode === 'hidden' ? mode : 'solid';
        applyTwinMode();
        schedule();
      },
      setTheme: function (name) {
        var next = name === 'dark' ? 'dark' : 'light';
        if (next === themeName) return;
        themeName = next;
        rebuildEverything();
        schedule();
      },
      /** Ease the camera to frame an entity, or the whole station when called with null. */
      focus: function (entityId) {
        var e = entityId ? entityById[entityId] : null;
        if (!e) { flyTo(HOME.target.clone(), HOME.radius, HOME.theta, HOME.phi); schedule(); return; }
        var size = Math.max(e.size[0], e.size[1], e.size[2]);
        flyTo(v3(e.position), clamp(size * 3.4 + 1.9, 2.2, 9));
        schedule();
      },
      /**
       * Where an element currently is on screen, in CSS px relative to the container, or null if it is not shown.
       * Handy for tutorial hotspots and tests.
       */
      locate: function (buildId, elementId) {
        var b = findBuild(buildId);
        if (!b) return null;
        for (var i = 0; i < b.views.length; i++) {
          var v = b.views[i];
          if (v.el.id !== elementId || !v.wanted) continue;
          var anchor = v.pinDisc || v.pill || v.group;
          var p = anchor.getWorldPosition(new THREE.Vector3()).project(camera);
          if (p.z > 1) return null;
          return { x: (p.x + 1) / 2 * canvas.clientWidth, y: (1 - p.y) / 2 * canvas.clientHeight };
        }
        return null;
      },
      /** Subscribe to { type: 'step' | 'checklist' | 'click', ... }. Returns an unsubscribe function. */
      onEvent: function (cb) {
        listeners.push(cb);
        return function () { var i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); };
      },
      resize: resize,
      dispose: function () {
        if (disposed) return;
        disposed = true;
        if (raf) cancelAnimationFrame(raf);
        if (io) io.disconnect();
        if (ro) ro.disconnect(); else window.removeEventListener('resize', resize);
        document.removeEventListener('visibilitychange', onVisibility);
        canvas.removeEventListener('pointerdown', onPointerDown);
        canvas.removeEventListener('pointermove', onPointerMove);
        canvas.removeEventListener('pointerup', onPointerUp);
        canvas.removeEventListener('pointercancel', onPointerUp);
        canvas.removeEventListener('pointerleave', onLeave);
        canvas.removeEventListener('wheel', onWheel);
        canvas.removeEventListener('contextmenu', onContext);
        disposables.forEach(function (d) { if (d && d.dispose) d.dispose(); });
        renderer.dispose();
        if (canvas.parentNode) canvas.parentNode.removeChild(canvas);
        listeners.length = 0;
      },
      /** The canvas element, for callers that want to style or screenshot it. */
      canvas: canvas
    };

    applyBackground();
    buildRoom();
    buildTwin();
    resize();
    applyCamera();
    schedule();

    // Canvas text is drawn with whatever font is available; redraw once web fonts have loaded.
    if (document.fonts && document.fonts.ready && document.fonts.status !== 'loaded') {
      document.fonts.ready.then(function () { if (!disposed) { measureCtx = null; rebuildEverything(); schedule(); } });
    }

    return api;
  }

  window.SpatailSim = { create: create, version: '1.0.0' };
})();
