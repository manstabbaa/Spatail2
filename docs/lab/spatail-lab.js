/* Spatail Space Lab: a browser host for the Spatail runtime.
 *
 * The devices run generated programs inside a sandboxed JavaScript engine with the Spatail library loaded
 * (packages/com.spatail.core/.../Script/spatail.js.txt). This file is a second host for that same library: it
 * implements the `__host` contract over Three.js so the same programs run here, unchanged.
 *
 * The lab also stands in for the device's senses. The world is plain geometry that programs can only learn about
 * the way a headset does: by casting rays into it, by pointing at a spot in a captured camera frame, and from a
 * list of flat surfaces that starts empty and grows as the user looks around. Nothing is scanned ahead of time.
 *
 * Plain script, no build step. Expects a global THREE (r128), window.SPATAIL_DATA and the library source.
 */
(function () {
  'use strict';

  var DEG = Math.PI / 180;

  var THEMES = {
    light: {
      bgTop: '#FFFFFF', bgBottom: '#ECE8FA', floor: '#F3F0FB', road: '#E4E0F2', grid: '#DAD4F0', wall: '#FAF9FE',
      body: '#FFFFFF', shade: '#E7E3F4', dark: '#5A5672', glass: '#CFC9F2', edge: '#9C95C4',
      outline: '#6C5CE7', marker: '#6C5CE7',
      hemiSky: 0xffffff, hemiGround: 0xcfc8ec, hemi: 0.92, sun: 0.22
    },
    dark: {
      bgTop: '#191822', bgBottom: '#0E0D14', floor: '#1A1924', road: '#14131C', grid: '#2B293B', wall: '#15141D',
      body: '#312F42', shade: '#282637', dark: '#14131C', glass: '#4A4580', edge: '#9A90F2',
      outline: '#A79DF7', marker: '#A79DF7',
      hemiSky: 0xffffff, hemiGround: 0x6a64a0, hemi: 0.95, sun: 0.3
    }
  };
  var FONT_STACK = 'Inter, "Helvetica Neue", Arial, system-ui, sans-serif';

  // ---- Unity (left-handed, +Z forward) <-> Three (right-handed) --------------------------------
  // Positions flip Z; rotations flip the X and Y components. Everything crossing the host boundary goes
  // through these, so the library and the programs only ever see Unity conventions.

  function toV3(a) { return new THREE.Vector3(a[0], a[1], -a[2]); }
  function fromV3(v) { return [round(v.x), round(v.y), round(-v.z)]; }
  function toQuat(a) { return new THREE.Quaternion(-a[0], -a[1], a[2], a[3]); }
  function eulerDeg(a) { return new THREE.Euler(-(a[0] || 0) * DEG, -(a[1] || 0) * DEG, (a[2] || 0) * DEG, 'YXZ'); }
  function round(n) { return Math.round(n * 10000) / 10000; }
  function clamp(v, a, b) { return v < a ? a : (v > b ? b : v); }

  // Unity-convention quaternion maths for the user's head.
  function qAxis(axis, deg) { var h = deg * DEG / 2, s = Math.sin(h); return [axis[0] * s, axis[1] * s, axis[2] * s, Math.cos(h)]; }
  function qMul(a, b) {
    return [a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1], a[3] * b[1] + a[1] * b[3] + a[2] * b[0] - a[0] * b[2],
      a[3] * b[2] + a[2] * b[3] + a[0] * b[1] - a[1] * b[0], a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]];
  }
  function qRot(q, v) {
    var x2 = q[0] * 2, y2 = q[1] * 2, z2 = q[2] * 2, xx = q[0] * x2, yy = q[1] * y2, zz = q[2] * z2;
    var xy = q[0] * y2, xz = q[0] * z2, yz = q[1] * z2, wx = q[3] * x2, wy = q[3] * y2, wz = q[3] * z2;
    return [(1 - (yy + zz)) * v[0] + (xy - wz) * v[1] + (xz + wy) * v[2], (xy + wz) * v[0] + (1 - (xx + zz)) * v[1] + (yz - wx) * v[2],
      (xz - wy) * v[0] + (yz + wx) * v[1] + (1 - (xx + yy)) * v[2]];
  }
  function qInv(q) { return [-q[0], -q[1], -q[2], q[3]]; }
  function yawQuat(deg) { return qAxis([0, 1, 0], deg); }

  // ---- seeded randomness -----------------------------------------------------------------------

  function rng(seed) {
    var a = (seed >>> 0) || 1;
    var next = function () {
      a |= 0; a = a + 0x6D2B79F5 | 0;
      var t = Math.imul(a ^ a >>> 15, 1 | a);
      t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
      return ((t ^ t >>> 14) >>> 0) / 4294967296;
    };
    return {
      next: next,
      range: function (lo, hi) { return lo + (hi - lo) * next(); },
      int: function (lo, hi) { return Math.floor(lo + (hi - lo + 1) * next()); },
      pick: function (list) { return list[Math.floor(next() * list.length)]; },
      chance: function (p) { return next() < p; }
    };
  }

  // ---- text metrics: the same maths as FontMetrics.cs, on the same numbers -------------------------

  function fontOf(fonts, weight) { return fonts[weight === 'semibold' ? 'semibold' : 'medium'] || fonts.medium; }
  function glyph(font, ch) { return font.glyphs[ch.charCodeAt(0)] || font.glyphs['63']; }
  function widthEm(font, s) {
    var w = 0;
    for (var i = 0; i < s.length; i++) { var g = glyph(font, s.charAt(i)); if (g) w += g.a; }
    return w;
  }
  function wrapEm(font, text, maxEm) {
    var lines = [];
    String(text === undefined || text === null ? '' : text).split('\n').forEach(function (paragraph) {
      if (!(maxEm > 0)) { lines.push(paragraph); return; }
      var line = '', width = 0, space = widthEm(font, ' ');
      paragraph.split(' ').forEach(function (word) {
        if (!word.length) return;
        var w = widthEm(font, word);
        if (line.length > 0 && width + space + w > maxEm) { lines.push(line); line = ''; width = 0; }
        if (line.length > 0) { line += ' '; width += space; }
        line += word; width += w;
      });
      lines.push(line);
    });
    return lines;
  }
  function measureBlock(fonts, text, size, weight, maxWidth) {
    var font = fontOf(fonts, weight);
    var lines = wrapEm(font, text, maxWidth > 0 && size > 0 ? maxWidth / size : 0);
    var width = 0;
    lines.forEach(function (l) { width = Math.max(width, widthEm(font, l) * size); });
    return { width: width, height: Math.max(0, lines.length - 1) * font.lineHeight * size + font.capHeight * size, lines: lines, font: font };
  }

  // ---- geometry authored in Unity coordinates -----------------------------------------------------

  function unityGeometry(positions, indices, uvs, colors) {
    var n = positions.length / 3, p = new Float32Array(positions.length);
    for (var i = 0; i < n; i++) { p[i * 3] = positions[i * 3]; p[i * 3 + 1] = positions[i * 3 + 1]; p[i * 3 + 2] = -positions[i * 3 + 2]; }
    var idx = new Array(indices.length);
    // Mirroring flips the winding, so swap two corners of every triangle.
    for (var t = 0; t < indices.length; t += 3) { idx[t] = indices[t]; idx[t + 1] = indices[t + 2]; idx[t + 2] = indices[t + 1]; }
    var g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(p, 3));
    if (uvs) g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(uvs), 2));
    if (colors) g.setAttribute('color', new THREE.BufferAttribute(new Float32Array(colors), 3));
    g.setIndex(idx);
    g.computeVertexNormals();
    return g;
  }

  // Straight tube segments through points (given in Unity coordinates); UV.x carries distance along the path in
  // metres and UV.y runs around the tube, which is what the flow shader expects.
  // `toward` (optional, Three space) is where the viewer stands: the seam where the chevrons point downstream is
  // turned to face it, so the direction also reads in a still frame.
  function tubeGeometry(points, radius, sides, toward) {
    var positions = [], normals = [], uvs = [], dist = 0, up = new THREE.Vector3(0, 1, 0);
    for (var s = 0; s < points.length - 1; s++) {
      var a = toV3(points[s]), b = toV3(points[s + 1]), axis = b.clone().sub(a), len = axis.length();
      if (len < 1e-5) continue;
      var g = new THREE.CylinderGeometry(radius, radius, len, sides, 1, true).toNonIndexed();
      var mid = a.clone().add(b).multiplyScalar(0.5), turn = new THREE.Quaternion().setFromUnitVectors(up, axis.normalize());
      if (toward) {
        // u = 0 leaves the cylinder along its local +Z; twist about the axis until that points at the viewer.
        var want = toward.clone().sub(mid); want.addScaledVector(axis, -want.dot(axis));
        var have = new THREE.Vector3(0, 0, 1).applyQuaternion(turn);
        if (want.lengthSq() > 1e-8) {
          want.normalize();
          var angle = Math.atan2(new THREE.Vector3().crossVectors(have, want).dot(axis), have.dot(want));
          turn.premultiply(new THREE.Quaternion().setFromAxisAngle(axis, angle));
        }
      }
      var m = new THREE.Matrix4().compose(mid, turn, new THREE.Vector3(1, 1, 1));
      g.applyMatrix4(m);
      var p = g.attributes.position.array, n = g.attributes.normal.array, uv = g.attributes.uv.array;
      for (var i = 0; i < p.length; i++) { positions.push(p[i]); normals.push(n[i]); }
      // CylinderGeometry maps u around and v from the bottom (0) to the top (1); the bottom is the upstream end.
      for (var k = 0; k < uv.length; k += 2) uvs.push(dist + uv[k + 1] * len, uv[k]);
      g.dispose();
      dist += len;
    }
    var out = new THREE.BufferGeometry();
    out.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    out.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
    out.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
    return out;
  }

  // Ring in the local XZ plane. The arc starts on +X and sweeps toward Unity's +Z (anticlockwise seen from above).
  function torusGeometry(ring, tube, arcDeg, segments, sides) {
    var arc = (arcDeg === undefined ? 360 : arcDeg) * DEG;
    var g = new THREE.TorusGeometry(ring, tube, sides, segments, arc);
    g.rotateX(-Math.PI / 2);
    return g;
  }

  function roundedRectShape(w, h, r) {
    r = Math.max(0, Math.min(r, w / 2, h / 2));
    var x = -w / 2, y = -h / 2, s = new THREE.Shape();
    s.moveTo(x + r, y);
    s.lineTo(x + w - r, y); if (r > 0) s.absarc(x + w - r, y + r, r, -Math.PI / 2, 0, false);
    s.lineTo(x + w, y + h - r); if (r > 0) s.absarc(x + w - r, y + h - r, r, 0, Math.PI / 2, false);
    s.lineTo(x + r, y + h); if (r > 0) s.absarc(x + r, y + h - r, r, Math.PI / 2, Math.PI, false);
    s.lineTo(x, y + r); if (r > 0) s.absarc(x + r, y + r, r, Math.PI, Math.PI * 1.5, false);
    return s;
  }

  // ---- the spaces ------------------------------------------------------------------------------
  // A space is: solids (what rays hit), planes (what plane detection can discover), optional stand-in props, and
  // where the user starts. Solids reuse the stand-in ShapeDef vocabulary: { t, p, s, r, tone } inside an object frame.

  function box(p, s, tone, r) { var d = { t: 'box', p: p, s: s }; if (tone) d.tone = tone; if (r) d.r = r; return d; }
  function cyl(p, radius, length, r, tone, top) { var d = { t: 'cyl', p: p, s: [radius, length, top || 0] }; if (tone) d.tone = tone; if (r) d.r = r; return d; }
  function tor(p, ring, tube, r, tone) { var d = { t: 'torus', p: p, s: [ring, tube, 0] }; if (tone) d.tone = tone; if (r) d.r = r; return d; }
  function sph(p, radius, tone) { var d = { t: 'sphere', p: p, s: [radius, 0, 0] }; if (tone) d.tone = tone; return d; }
  var AX = [0, 0, 90], AZ = [90, 0, 0];   // lay a cylinder along X or Z

  function wallPlane(id, label, centre, yawDeg, width, height) {
    return { id: id, kind: 'wall', label: label, position: centre, rotation: yawQuat(yawDeg), size: [width, height, 0.02] };
  }
  function floorPlane(id, centre, width, depth) {
    return { id: id, kind: 'floor', label: 'Floor', position: centre, rotation: [0, 0, 0, 1], size: [width, 0, depth] };
  }
  // The wall behind the wearer and the ceiling are real to the sensors but hidden from the overview camera, which
  // looks in from that side.
  function backdrop(plane) { plane.backdrop = true; return plane; }
  function ceilingPlane(centre, width, depth) {
    // Turned over, so its local up points down into the room.
    return backdrop({ id: 'ceiling', kind: 'ceiling', label: 'Ceiling', position: centre, rotation: [1, 0, 0, 0], size: [width, 0, depth] });
  }
  function roomShell(width, depth, height, backZ) {
    var mid = backZ - depth / 2;
    return [
      floorPlane('floor', [0, 0, mid], width, depth),
      wallPlane('wall-back', 'Wall', [0, height / 2, backZ], 180, width, height),
      wallPlane('wall-left', 'Wall', [-width / 2, height / 2, mid], 90, depth, height),
      wallPlane('wall-right', 'Wall', [width / 2, height / 2, mid], -90, depth, height),
      backdrop(wallPlane('wall-front', 'Wall', [0, height / 2, backZ - depth], 0, width, height)),
      ceilingPlane([0, height, mid], width, depth)
    ];
  }

  function workshop(data) {
    var planes = [], objects = [], props = [];
    data.room.entities.forEach(function (e) {
      if (e.kind === 'floor' || e.kind === 'wall') { planes.push({ id: e.id, kind: e.kind, label: e.label, position: e.position, rotation: e.rotation, size: e.size }); return; }
      if (!e.virtual) return;
      // Plane detection finds a bench top on a headset whether or not anything is known about the bench.
      if (e.kind === 'table') planes.push({ id: e.id + '-top', kind: 'table', label: 'Table', position: e.position, rotation: e.rotation, size: e.size });
      props.push(e);
      if (e.shapes) objects.push({ origin: e.origin, rotation: e.rotation, shapes: e.shapes, propId: e.id, label: e.label });
    });
    // The film's room has three walls; close it behind the wearer the same way the generated rooms are.
    planes.push(backdrop(wallPlane('wall-front', 'Wall', [0, 1.4, -5.05], 0, 7, 2.8)), ceilingPlane([0, 2.8, -2.05], 7, 6));
    return { planes: planes, objects: objects, props: props, user: { position: [-0.9, 1.6, -2.5], yaw: 22, pitch: 16 }, bounds: [3.3, 3.2, -4.9, 0.8], orbit: [0, 0.6, 0.2] };
  }

  function valveShapes(x, y, z, pipeR) {
    return [
      cyl([x - 0.1, y, z], pipeR * 1.6, 0.025, AX, 'shade'), cyl([x + 0.1, y, z], pipeR * 1.6, 0.025, AX, 'shade'),
      cyl([x, y, z], pipeR * 1.35, 0.17, AX), cyl([x, y + 0.14, z], 0.05, 0.14), cyl([x, y + 0.1, z], 0.075, 0.02, null, 'shade'),
      cyl([x, y + 0.31, z], 0.011, 0.2, null, 'dark'), tor([x, y + 0.4, z], 0.1, 0.012),
      box([x, y + 0.4, z], [0.19, 0.012, 0.018]), box([x, y + 0.4, z], [0.19, 0.012, 0.018], null, [0, 90, 0]), cyl([x, y + 0.4, z], 0.024, 0.03, null, 'dark')
    ];
  }

  function plantRoom(seed) {
    var R = rng(seed * 7919 + 13), W = 8, D = 7, Hh = 3.2, backZ = 3;
    var planes = roomShell(W, D, Hh, backZ), objects = [];
    var count = R.int(2, 3), xs = [], spacing = 5.2 / count, x0 = -2.6 + spacing / 2;
    var headerY = R.range(0.85, 1.15), headerZ = 0, pipeR = R.pick([0.05, 0.06, 0.07]), minFront = 9;
    var tanks = [];
    for (var i = 0; i < count; i++) {
      var tx = x0 + i * spacing + R.range(-0.35, 0.35), tr = R.range(0.42, 0.68), th = R.range(1.5, 2.3), tz = backZ - tr - R.range(0.35, 0.6);
      tanks.push({ x: tx, r: tr, h: th, z: tz });
      xs.push(tx);
      minFront = Math.min(minFront, tz - tr);
    }
    headerZ = minFront - R.range(0.28, 0.42);
    tanks.forEach(function (t, i) {
      var s = [box([0, 0.06, 0], [t.r * 2.3, 0.12, t.r * 2.3], 'shade'), cyl([0, 0.12 + t.h / 2, 0], t.r, t.h),
        cyl([0, 0.12 + t.h + 0.09, 0], t.r, 0.18, null, null, t.r * 0.55), cyl([0, 0.12 + t.h + 0.22, 0], 0.11, 0.08, null, 'shade')];
      for (var b = 1; b <= 2; b++) s.push(cyl([0, 0.12 + t.h * b / 3, 0], t.r + 0.012, 0.03, null, 'shade'));
      // Sight glass and nameplate on the side facing the room.
      s.push(box([t.r * 0.55, 0.12 + t.h * 0.55, -t.r * 0.86], [0.05, t.h * 0.5, 0.03], 'glass'));
      s.push(box([-t.r * 0.3, 0.12 + t.h * 0.72, -t.r * 0.97], [0.22, 0.12, 0.012], 'dark'));
      // Nozzle down to the header.
      var drop = t.z - t.r - headerZ;
      s.push(cyl([0, headerY, -t.r - drop / 2], pipeR, drop + 0.04, AZ), cyl([0, headerY, -t.r - 0.03], pipeR * 1.6, 0.025, AZ, 'shade'));
      objects.push({ origin: [t.x, 0, t.z], shapes: s, label: 'tank ' + (i + 1) });
    });
    // Header pipe with flanges, supports, valves and a gauge.
    var left = Math.min.apply(null, xs) - R.range(0.5, 0.9), right = Math.max.apply(null, xs) + R.range(0.5, 0.9), len = right - left, mid = (left + right) / 2;
    var header = [cyl([mid, headerY, headerZ], pipeR, len, AX), cyl([left, headerY, headerZ], pipeR * 1.7, 0.03, AX, 'shade'), cyl([right, headerY, headerZ], pipeR * 1.7, 0.03, AX, 'shade')];
    for (var sx = left + 0.5; sx < right - 0.3; sx += R.range(1.1, 1.6)) header.push(box([sx, (headerY - pipeR) / 2, headerZ], [0.05, headerY - pipeR, 0.05]), box([sx, 0.008, headerZ], [0.18, 0.016, 0.18], 'shade'));
    var valves = R.int(2, 3);
    for (var v = 0; v < valves; v++) {
      var vx = left + (v + 0.5) * len / valves + R.range(-0.25, 0.25);
      header = header.concat(valveShapes(vx, headerY, headerZ, pipeR));
    }
    var gx = xs[0] + R.range(0.5, 0.9);
    header.push(cyl([gx, headerY + pipeR + 0.06, headerZ], 0.012, 0.12, null, 'dark'), cyl([gx, headerY + pipeR + 0.17, headerZ], 0.07, 0.035, AZ), cyl([gx, headerY + pipeR + 0.17, headerZ - 0.02], 0.056, 0.005, AZ, 'shade'));
    // The header turns up the wall at one end.
    var riserX = R.chance(0.5) ? left : right;
    header.push(cyl([riserX, headerY + (Hh - headerY) / 2 - 0.2, headerZ], pipeR, Hh - headerY - 0.4));
    objects.push({ origin: [0, 0, 0], shapes: header, label: 'pipe header' });

    // Electrical cabinet against a side wall.
    var side = R.chance(0.5) ? -1 : 1, cz = R.range(-1.6, 0.6);
    objects.push({
      origin: [side * (W / 2 - 0.26), 0, cz], yaw: side < 0 ? 90 : -90, label: 'electrical cabinet',
      shapes: [box([0, 0.97, 0], [0.9, 1.9, 0.45]), box([0, 1.0, -0.23], [0.8, 1.7, 0.012], 'shade'), box([0.33, 1.0, -0.245], [0.025, 0.14, 0.03], 'dark'),
        cyl([-0.25, 1.72, -0.24], 0.02, 0.014, AZ, 'dark'), cyl([-0.17, 1.72, -0.24], 0.02, 0.014, AZ, 'dark'), cyl([-0.09, 1.72, -0.24], 0.02, 0.014, AZ, 'dark'),
        box([0, 0.03, 0], [0.94, 0.06, 0.49], 'shade'), cyl([0.25, 2.5, 0.1], 0.02, 1.25, null, 'shade')]
    });
    // A pallet with cartons, and bollards guarding it.
    var px = -side * R.range(1.2, 2.4), pz = R.range(-2.6, -1.2), pyaw = R.range(-25, 25), pallet = [];
    for (var sl = 0; sl < 5; sl++) pallet.push(box([-0.5 + sl * 0.25, 0.13, 0], [0.14, 0.022, 0.8], 'shade'));
    for (var rn = -1; rn <= 1; rn++) pallet.push(box([0, 0.06, rn * 0.34], [1.14, 0.1, 0.08]));
    var cartons = R.int(2, 4);
    for (var cb = 0; cb < cartons; cb++) {
      var cs = [R.range(0.3, 0.5), R.range(0.25, 0.45), R.range(0.3, 0.42)];
      pallet.push(box([-0.3 + (cb % 2) * 0.6 + R.range(-0.05, 0.05), 0.141 + cs[1] / 2 + (cb > 1 ? 0.42 : 0), R.range(-0.12, 0.12)], cs, cb % 2 ? 'shade' : null, [0, R.range(-12, 12), 0]));
    }
    objects.push({ origin: [px, 0, pz], yaw: pyaw, shapes: pallet, label: 'pallet' });
    var bollards = R.int(2, 4);
    for (var bo = 0; bo < bollards; bo++) {
      objects.push({ origin: [px - 0.9 + bo * 0.6, 0, pz - 0.75], label: 'bollard',
        shapes: [cyl([0, 0.45, 0], 0.06, 0.9), cyl([0, 0.78, 0], 0.064, 0.1, null, 'dark'), cyl([0, 0.905, 0], 0.045, 0.02, null, 'shade'), cyl([0, 0.01, 0], 0.11, 0.02, null, 'shade')] });
    }
    return { planes: planes, objects: objects, props: [], user: { position: [R.range(-0.6, 0.6), 1.6, -2.7], yaw: R.range(-10, 10), pitch: 10 }, bounds: [W / 2 - 0.3, Hh, backZ - D + 0.3, backZ - 0.3], orbit: [0, 0.8, 0.6] };
  }

  function office(seed) {
    var R = rng(seed * 104729 + 7), W = 7, D = 6, Hh = 2.8, backZ = 2.6;
    var planes = roomShell(W, D, Hh, backZ), objects = [];
    var desks = R.int(2, 3);
    for (var i = 0; i < desks; i++) {
      var dx = -2.2 + i * (4.4 / Math.max(1, desks - 1)) + R.range(-0.3, 0.3), dz = R.range(-0.6, 1.2), yaw = R.range(-14, 14);
      if (desks === 2) dx = (i ? 1.4 : -1.4) + R.range(-0.3, 0.3);
      var dw = R.range(1.2, 1.6), dd = 0.72, dh = 0.74;
      var s = [box([0, dh - 0.015, 0], [dw, 0.03, dd])];
      [[-1, -1], [1, -1], [-1, 1], [1, 1]].forEach(function (c) { s.push(box([c[0] * (dw / 2 - 0.04), (dh - 0.03) / 2, c[1] * (dd / 2 - 0.04)], [0.04, dh - 0.03, 0.04], 'shade')); });
      var monitors = R.int(1, 2);
      for (var m = 0; m < monitors; m++) {
        var mx = monitors === 1 ? 0 : (m ? 0.3 : -0.3);
        s.push(box([mx, dh + 0.008, 0.18], [0.2, 0.016, 0.14], 'shade'), box([mx, dh + 0.1, 0.2], [0.03, 0.2, 0.03], 'shade'),
          box([mx, dh + 0.33, 0.19], [0.54, 0.33, 0.02], 'dark', [0, m ? -8 : (monitors === 2 ? 8 : 0), 0]));
      }
      s.push(box([0, dh + 0.008, -0.14], [0.4, 0.016, 0.13], 'shade'), box([0.32, dh + 0.012, -0.14], [0.06, 0.024, 0.1], 'shade'));
      if (R.chance(0.6)) s.push(cyl([-dw / 2 + 0.15, dh + 0.05, 0.1], 0.04, 0.1), cyl([-dw / 2 + 0.15, dh + 0.1, 0.1], 0.03, 0.004, null, 'dark'));
      objects.push({ origin: [dx, 0, dz], yaw: yaw, shapes: s, label: 'desk ' + (i + 1) });
      // The desk top is something plane detection can find.
      planes.push({ id: 'table-' + (i + 1), kind: 'table', label: 'Table', position: [dx, dh / 2, dz], rotation: yawQuat(yaw), size: [dw, dh, dd] });
      // Chair, pushed back a little and turned.
      var chairYaw = yaw + R.range(-35, 35), back = qRot(yawQuat(yaw), [R.range(-0.15, 0.15), 0, -dd / 2 - R.range(0.35, 0.6)]);
      objects.push({ origin: [dx + back[0], 0, dz + back[2]], yaw: chairYaw, label: 'chair',
        shapes: [cyl([0, 0.03, 0], 0.27, 0.03, null, 'dark'), cyl([0, 0.24, 0], 0.025, 0.4, null, 'dark'), box([0, 0.47, 0], [0.46, 0.07, 0.46]),
          box([0, 0.78, -0.22], [0.42, 0.5, 0.05]), box([0, 0.55, -0.21], [0.04, 0.12, 0.03], 'dark')] });
    }
    // Shelving against a side wall.
    var side = R.chance(0.5) ? -1 : 1, sz = R.range(-1.4, 1.2), shelf = [box([0, 0.95, 0.15], [1.0, 1.9, 0.02], 'shade'), box([-0.49, 0.95, 0], [0.02, 1.9, 0.32]), box([0.49, 0.95, 0], [0.02, 1.9, 0.32])];
    for (var sh = 0; sh < 5; sh++) {
      var sy = 0.04 + sh * 0.46;
      shelf.push(box([0, sy, 0], [0.96, 0.025, 0.32]));
      if (sh < 4) {
        var bx = -0.42;
        while (bx < 0.36) {
          var bw = R.range(0.03, 0.07), bh = R.range(0.22, 0.34);
          if (R.chance(0.82)) shelf.push(box([bx + bw / 2, sy + 0.0125 + bh / 2, R.range(-0.02, 0.03)], [bw, bh, 0.2], R.pick([null, 'shade', 'glass', 'dark'])));
          bx += bw + 0.008;
        }
      }
    }
    objects.push({ origin: [side * (W / 2 - 0.19), 0, sz], yaw: side < 0 ? 90 : -90, shapes: shelf, label: 'shelves' });
    // Whiteboard on the back wall.
    var wx = R.range(-1.6, 1.6);
    objects.push({ origin: [wx, 1.5, backZ - 0.02], label: 'whiteboard',
      shapes: [box([0, 0, 0], [1.9, 1.15, 0.03], 'shade'), box([0, 0, -0.018], [1.8, 1.05, 0.008]), box([0, -0.6, -0.05], [1.2, 0.03, 0.08], 'shade'),
        box([-0.3, -0.575, -0.05], [0.12, 0.02, 0.025], 'dark'), box([0.1, -0.575, -0.05], [0.1, 0.02, 0.025], 'glass')] });
    // A door in the other side wall.
    var doorZ = R.range(-1.8, 1.4);
    objects.push({ origin: [-side * (W / 2 - 0.03), 0, doorZ], yaw: -side < 0 ? 90 : -90, label: 'door',
      shapes: [box([0, 1.04, 0], [1.04, 2.12, 0.05], 'dark'), box([0, 1.02, -0.012], [0.9, 2.03, 0.045], 'shade'), cyl([0.36, 1.02, -0.06], 0.014, 0.12, AX, 'dark'), box([0.36, 1.02, -0.04], [0.05, 0.16, 0.012])] });
    // A plant in a corner.
    if (R.chance(0.7)) objects.push({ origin: [side * (W / 2 - 0.5), 0, backZ - 0.5], label: 'plant',
      shapes: [cyl([0, 0.2, 0], 0.17, 0.4, null, 'shade', 0.22), cyl([0, 0.65, 0], 0.02, 0.5, null, 'dark'), sph([0, 1.05, 0], 0.3), sph([0.15, 0.85, 0.05], 0.2, 'shade'), sph([-0.14, 0.9, -0.06], 0.18, 'glass')] });
    return { planes: planes, objects: objects, props: [], user: { position: [R.range(-0.5, 0.5), 1.6, -2.7], yaw: R.range(-8, 8), pitch: 12 }, bounds: [W / 2 - 0.3, Hh, backZ - D + 0.3, backZ - 0.3], orbit: [0, 0.7, 0.2] };
  }

  function street(seed) {
    var R = rng(seed * 15485863 + 3), L = 13, frontZ = 2.4, kerbZ = -1.1;
    var planes = [floorPlane('floor', [0, 0, (frontZ + kerbZ) / 2], L, frontZ - kerbZ), wallPlane('wall-shopfront', 'Wall', [0, 1.8, frontZ], 180, L, 3.6),
      backdrop(wallPlane('wall-far', 'Wall', [0, 2.4, kerbZ - 6.4], 0, L, 4.8))];
    var objects = [];
    // The far side of the road: a plain frontage with a few windows.
    var far = [];
    for (var fx = -L / 2 + 1.3 + R.range(0, 0.6); fx < L / 2 - 1; fx += R.range(2.1, 2.9)) far.push(box([fx, R.chance(0.3) ? 1.25 : 1.75, 0.02], [1.2, R.chance(0.5) ? 1.5 : 1.1, 0.04], 'glass'), box([fx, 3.6, 0.02], [1.2, 0.9, 0.04], 'glass'));
    objects.push({ origin: [0, 0, kerbZ - 6.4], shapes: far, label: 'far frontage', backdrop: true });
    // Kerb and the road below it.
    objects.push({ origin: [0, 0, kerbZ], shapes: [box([0, -0.03, -0.075], [L, 0.14, 0.15], 'shade')], label: 'kerb' });
    objects.push({ origin: [0, -0.12, kerbZ - 3.2], shapes: [box([0, -0.02, 0], [L, 0.04, 6.3], 'road')], label: 'road', flat: true });
    // Shopfront: door, window, sign and awning on the wall.
    var doorX = R.range(-2.4, 2.4), winSide = R.chance(0.5) ? -1 : 1, winX = doorX + winSide * R.range(1.9, 2.6), winW = R.range(1.8, 2.6);
    objects.push({ origin: [doorX, 0, frontZ], label: 'door',
      shapes: [box([0, 1.08, -0.03], [1.1, 2.2, 0.08], 'dark'), box([0, 1.05, -0.06], [0.94, 2.06, 0.04], 'shade'), box([0, 1.4, -0.085], [0.6, 0.9, 0.01], 'glass'),
        cyl([0.36, 1.05, -0.11], 0.014, 0.3, null, 'dark'), box([0, 0.02, -0.25], [1.2, 0.04, 0.4], 'shade')] });
    objects.push({ origin: [winX, 0, frontZ], label: 'shop window',
      shapes: [box([0, 1.45, -0.03], [winW + 0.12, 1.62, 0.08], 'dark'), box([0, 1.45, -0.06], [winW, 1.5, 0.03], 'glass'), box([0, 0.62, -0.09], [winW + 0.2, 0.05, 0.2], 'shade'),
        box([0, 1.45, -0.08], [0.03, 1.5, 0.02], 'dark')] });
    objects.push({ origin: [(doorX + winX) / 2, 2.75, frontZ], label: 'sign', shapes: [box([0, 0, -0.05], [Math.abs(winX - doorX) + winW * 0.5 + 1.0, 0.42, 0.08], 'shade'), box([0, 0, -0.095], [1.4, 0.16, 0.01], 'dark')] });
    if (R.chance(0.7)) objects.push({ origin: [winX, 2.38, frontZ], label: 'awning', shapes: [box([0, 0, -0.42], [winW + 0.3, 0.03, 0.9], null, [-18, 0, 0]), box([0, -0.16, -0.84], [winW + 0.3, 0.1, 0.02], 'shade')] });
    // Street furniture along the kerb.
    var lampX = R.range(-3.5, 3.5);
    objects.push({ origin: [lampX, 0, kerbZ + 0.35], label: 'lamp post',
      shapes: [cyl([0, 0.15, 0], 0.09, 0.3, null, 'shade'), cyl([0, 2.1, 0], 0.05, 3.9), box([0, 4.02, -0.35], [0.06, 0.06, 0.8]), box([0, 3.96, -0.72], [0.22, 0.08, 0.36], 'shade'), box([0, 3.915, -0.72], [0.16, 0.012, 0.28], 'glass')] });
    var binX = lampX + (R.chance(0.5) ? -1 : 1) * R.range(0.8, 1.6);
    objects.push({ origin: [binX, 0, kerbZ + 0.45], label: 'bin', shapes: [cyl([0, 0.4, 0], 0.22, 0.8), cyl([0, 0.78, 0], 0.235, 0.08, null, 'dark'), cyl([0, 0.83, 0], 0.2, 0.02, null, 'shade'), cyl([0, 0.02, 0], 0.24, 0.04, null, 'shade')] });
    var benchX = winX + R.range(-0.4, 0.4);
    objects.push({ origin: [benchX, 0, frontZ - 0.42], label: 'bench',
      shapes: [box([0, 0.44, 0], [1.5, 0.05, 0.4]), box([0, 0.72, 0.17], [1.5, 0.4, 0.05]), box([-0.62, 0.22, 0], [0.06, 0.44, 0.36], 'dark'), box([0.62, 0.22, 0], [0.06, 0.44, 0.36], 'dark')] });
    if (R.chance(0.75)) {
      var treeX = lampX + (binX > lampX ? -1 : 1) * R.range(2.2, 3.4);
      objects.push({ origin: [treeX, 0, kerbZ + 0.5], label: 'tree', shapes: [box([0, 0.01, 0], [0.9, 0.02, 0.9], 'dark'), cyl([0, 1.1, 0], 0.09, 2.2, null, 'shade', 0.06), sph([0, 2.7, 0], 0.85), sph([0.45, 2.3, 0.2], 0.5, 'shade'), sph([-0.4, 2.4, -0.15], 0.55, 'glass')] });
    }
    // A parked car on the road, built from boxes.
    var carX = R.range(-2.5, 2.5), carDir = R.chance(0.5) ? 0 : 180;
    var car = [box([0, 0.5, 0], [4.2, 0.55, 1.72]), box([-0.15, 1.02, 0], [2.3, 0.52, 1.56]), box([-0.15, 1.04, 0], [2.34, 0.4, 1.5], 'glass'), box([1.9, 0.62, 0], [0.42, 0.22, 1.6], 'shade'),
      box([2.09, 0.56, 0.6], [0.04, 0.12, 0.3], 'glass'), box([2.09, 0.56, -0.6], [0.04, 0.12, 0.3], 'glass'), box([-2.09, 0.6, 0], [0.04, 0.12, 1.5], 'dark')];
    [[-1.35, -0.86], [1.35, -0.86], [-1.35, 0.86], [1.35, 0.86]].forEach(function (w) { car.push(cyl([w[0], 0.32, w[1]], 0.32, 0.22, AZ, 'dark'), cyl([w[0], 0.32, w[1] + (w[1] > 0 ? 0.115 : -0.115)], 0.17, 0.01, AZ, 'shade')); });
    objects.push({ origin: [carX, -0.12, kerbZ - 1.15], yaw: carDir, shapes: car, label: 'parked car' });
    return { planes: planes, objects: objects, props: [], open: true, user: (function () { var side = R.chance(0.5) ? -1 : 1; return { position: [side * R.range(4.0, 5.0), 1.6, kerbZ + 1.5], yaw: -side * R.range(74, 88), pitch: 9 }; })(), bounds: [L / 2 - 0.4, 3.6, kerbZ - 5.9, frontZ - 0.35], orbit: [0, 0.8, 0.4] };
  }

  var SPACES = [
    { name: 'Workshop', seeded: false, about: 'The room from the film, with a stand-in pump. The only space that has props.', make: function (seed, data) { return workshop(data); } },
    { name: 'Plant room', seeded: true, about: 'Tanks, a pipe header with valves and a gauge, a cabinet, a pallet. Nothing is labelled.', make: plantRoom },
    { name: 'Office', seeded: true, about: 'Desks, chairs, shelves, a whiteboard and a door. Nothing is labelled.', make: office },
    { name: 'Street', seeded: true, about: 'A pavement outside a shop: one wall, no ceiling, a parked car.', make: street }
  ];

  // ---- the lab ---------------------------------------------------------------------------------

  var LIMITS = { maxNodes: 4000, maxErrors: 30 };
  var SHADOWED = ['window', 'document', 'globalThis', 'self', 'fetch', 'XMLHttpRequest', 'WebSocket', 'localStorage', 'sessionStorage', 'navigator', 'location', 'parent', 'top', 'frames', 'require', 'process', 'importScripts'];
  var API = ['scene', 'space', 'user', 'voice', 'kit', 'vec', 'quat', 'theme', 'state', 'log', 'finish', 'console'];

  function create(container, options) {
    options = options || {};
    var data = options.data || window.SPATAIL_DATA;
    var librarySource = options.library || window.SPATAIL_LIBRARY;
    if (!window.THREE) throw new Error('SpatailLab needs THREE (r128) loaded first.');
    if (!data) throw new Error('SpatailLab needs SPATAIL_DATA (data/spatail-data.js).');
    if (!librarySource) throw new Error('SpatailLab needs the runtime library source (options.library).');

    var themeName = options.theme === 'dark' ? 'dark' : 'light';
    var T = THEMES[themeName];
    var listeners = [];
    var disposed = false;

    function emit(ev) {
      listeners.slice().forEach(function (cb) { try { cb(ev); } catch (e) { /* a listener must not break the lab */ } });
    }

    // ---- renderer, scene graph ----------------------------------------------------------------

    var renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: false });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    var canvas = renderer.domElement;
    canvas.style.display = 'block';
    canvas.style.width = '100%';
    canvas.style.height = '100%';
    canvas.style.touchAction = options.touchAction || 'none';
    canvas.style.outline = 'none';
    canvas.setAttribute('tabindex', '0');
    canvas.setAttribute('aria-label', 'Space lab view. Drag to look, W A S D or arrow keys to walk.');
    container.appendChild(canvas);

    var scene3 = new THREE.Scene();
    var realGroup = new THREE.Group();      // the world: what the device's sensors would see
    var contentGroup = new THREE.Group();   // what running programs have built
    var helperGroup = new THREE.Group();    // lab-only overlays: discovered-surface outlines, the user marker
    scene3.add(realGroup, contentGroup, helperGroup);
    var hemi = new THREE.HemisphereLight(T.hemiSky, T.hemiGround, T.hemi);
    var sun = new THREE.DirectionalLight(0xffffff, T.sun);
    sun.position.set(-2.5, 6, 3.5);
    scene3.add(hemi, sun);

    var userCam = new THREE.PerspectiveCamera(52, 1.6, 0.05, 80);
    var orbitCam = new THREE.PerspectiveCamera(42, 1.6, 0.05, 120);
    var captureCam = new THREE.PerspectiveCamera(60, 4 / 3, 0.05, 80);
    var CAPTURE_W = 960, CAPTURE_H = 720;
    var captureTarget = null;
    var mode = options.camera === 'orbit' ? 'orbit' : 'first';
    var orbit = { yaw: 28, pitch: 26, distance: 8, target: new THREE.Vector3(0, 0.6, 0) };

    // The user's head, in Unity coordinates.
    var head = { position: [0, 1.6, -2.5], yaw: 0, pitch: 12 };
    var bounds = [3.3, 3, -5, 0.8];
    function headQuat() { return qMul(yawQuat(head.yaw), qAxis([1, 0, 0], head.pitch)); }
    function headForward() { return qRot(headQuat(), [0, 0, 1]); }

    // ---- the real layer ------------------------------------------------------------------------

    var space = null, spaceName = '', spaceSeed = 1;
    var realMeshes = [];          // what sensing rays can hit
    var realMaterials = {};       // tone -> material, rebuilt on theme change
    var edgeMaterial = null, gridMaterial = null;
    var planes = [];              // { def, seen, found, samples[] }
    var discovered = [];
    var raycaster = new THREE.Raycaster();

    function toneMaterial(tone) {
      var key = tone || 'body';
      if (!realMaterials[key]) {
        var colour = T[key] || T.body;
        realMaterials[key] = key === 'glass'
          ? new THREE.MeshLambertMaterial({ color: colour, transparent: true, opacity: 0.72 })
          : new THREE.MeshLambertMaterial({ color: colour });
      }
      return realMaterials[key];
    }

    function shapeMesh(shape) {
      var geometry, s = shape.s || [1, 1, 1];
      if (shape.t === 'cyl') geometry = new THREE.CylinderGeometry(s[2] > 0 ? s[2] : s[0], s[0], s[1], 28);
      else if (shape.t === 'torus') geometry = torusGeometry(s[0], s[1], 360, 36, 10);
      else if (shape.t === 'sphere') geometry = new THREE.SphereGeometry(s[0], 22, 14);
      else geometry = new THREE.BoxGeometry(s[0], s[1], s[2]);
      var mesh = new THREE.Mesh(geometry, toneMaterial(shape.tone));
      mesh.position.copy(toV3(shape.p || [0, 0, 0]));
      if (shape.r) mesh.rotation.copy(eulerDeg(shape.r));
      if (shape.t !== 'sphere') {
        var edges = new THREE.LineSegments(new THREE.EdgesGeometry(geometry, shape.t === 'box' ? 1 : 32), edgeMaterial);
        edges.userData.helper = true;
        mesh.add(edges);
      }
      return mesh;
    }

    function clearGroup(group) {
      for (var i = group.children.length - 1; i >= 0; i--) {
        var child = group.children[i];
        group.remove(child);
        child.traverse(function (o) { if (o.geometry) o.geometry.dispose(); });
      }
    }

    var backdrops = [];           // holders shown to the wearer and the capture camera, hidden from the overview
    function buildSpace() {
      clearGroup(realGroup);
      clearGroup(helperGroup);
      realMeshes = [];
      backdrops = [];
      Object.keys(realMaterials).forEach(function (k) { realMaterials[k].dispose(); });
      realMaterials = {};
      if (edgeMaterial) edgeMaterial.dispose();
      if (gridMaterial) gridMaterial.dispose();
      edgeMaterial = new THREE.LineBasicMaterial({ color: T.edge, transparent: true, opacity: themeName === 'dark' ? 0.8 : 0.9 });
      gridMaterial = new THREE.LineBasicMaterial({ color: T.grid, transparent: true, opacity: 0.9 });

      // Surfaces: a slab behind each plane so there is something solid to hit and to see.
      space.planes.forEach(function (p) {
        if (p.kind === 'table') return;   // the furniture object provides the geometry
        var holder = new THREE.Group();
        holder.position.copy(toV3(p.position));
        holder.quaternion.copy(toQuat(p.rotation));
        var mesh;
        if (p.kind === 'ceiling') {
          mesh = new THREE.Mesh(new THREE.BoxGeometry(p.size[0], 0.04, p.size[2]), new THREE.MeshLambertMaterial({ color: T.wall }));
          mesh.position.y = -0.02;
        } else if (p.kind === 'floor') {
          mesh = new THREE.Mesh(new THREE.BoxGeometry(p.size[0], 0.04, p.size[2]), new THREE.MeshLambertMaterial({ color: T.floor }));
          mesh.position.y = -0.02;
          var pts = [], hx = p.size[0] / 2, hz = p.size[2] / 2;
          for (var x = Math.ceil(-hx * 2) / 2; x <= hx + 1e-6; x += 0.5) pts.push(x, 0.002, -hz, x, 0.002, hz);
          for (var z = Math.ceil(-hz * 2) / 2; z <= hz + 1e-6; z += 0.5) pts.push(-hx, 0.002, z, hx, 0.002, z);
          var grid = new THREE.LineSegments(new THREE.BufferGeometry().setAttribute('position', new THREE.Float32BufferAttribute(pts, 3)), gridMaterial);
          grid.userData.helper = true;
          holder.add(grid);
        } else {
          mesh = new THREE.Mesh(new THREE.BoxGeometry(p.size[0], p.size[1], 0.06), new THREE.MeshLambertMaterial({ color: T.wall }));
          // The plane is the face toward the room; in Three that face looks along -Z of the holder.
          mesh.position.z = 0.03;
          var rim = new THREE.LineSegments(new THREE.EdgesGeometry(mesh.geometry), gridMaterial);
          rim.userData.helper = true;
          mesh.add(rim);
        }
        mesh.userData.real = true;
        holder.add(mesh);
        if (p.backdrop) backdrops.push(holder);
        realGroup.add(holder);
        realMeshes.push(mesh);
      });

      space.objects.forEach(function (o) {
        var holder = new THREE.Group();
        if (o.backdrop) backdrops.push(holder);
        holder.position.copy(toV3(o.origin || [0, 0, 0]));
        if (o.rotation) holder.quaternion.copy(toQuat(o.rotation));
        else if (o.yaw) holder.quaternion.copy(toQuat(yawQuat(o.yaw)));
        o.shapes.forEach(function (shape) {
          var mesh = shapeMesh(shape);
          mesh.userData.real = true;
          // Only stand-in holograms know what they are. Generated objects stay anonymous to the programs.
          if (o.propId) { mesh.userData.label = o.label; mesh.userData.propId = o.propId; }
          holder.add(mesh);
          realMeshes.push(mesh);
        });
        realGroup.add(holder);
      });

      planes = space.planes.map(function (def) { return { def: def, seen: 0, found: false, samples: planeSamples(def) }; });
      discovered = [];
      buildMarker();
      realGroup.updateMatrixWorld(true);
    }

    function planeSamples(def) {
      var out = [], n = 5, q = def.rotation;
      for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) {
        var a = (i + 0.5) / n - 0.5, b = (j + 0.5) / n - 0.5, local;
        if (def.kind === 'wall') local = [a * def.size[0], b * def.size[1], 0];
        else if (def.kind === 'table') local = [a * def.size[0], def.size[1] / 2, b * def.size[2]];
        else local = [a * def.size[0], 0, b * def.size[2]];
        var w = qRot(q, local);
        out.push([def.position[0] + w[0], def.position[1] + w[1], def.position[2] + w[2]]);
      }
      return out;
    }

    // ---- sensing ---------------------------------------------------------------------------------

    function castReal(origin, direction, maxDistance) {
      raycaster.set(toV3(origin), toV3(direction).normalize());
      raycaster.near = 0;
      raycaster.far = maxDistance;
      var hits = raycaster.intersectObjects(realMeshes, false);
      if (!hits.length) return null;
      var hit = hits[0];
      var normal = hit.face ? hit.face.normal.clone().transformDirection(hit.object.matrixWorld) : new THREE.Vector3(0, 1, 0);
      if (normal.dot(raycaster.ray.direction) > 0) normal.negate();
      var out = { position: fromV3(hit.point), normal: fromV3(normal), distance: round(hit.distance) };
      if (hit.object.userData.label) out.label = hit.object.userData.label;
      return out;
    }

    function viewRay(view, u, v) {
      var cu = view.centerU === undefined ? 0.5 : view.centerU, cv = view.centerV === undefined ? 0.5 : view.centerV;
      var local = [(u - cu) * 2 * view.tanHalfX, -(v - cv) * 2 * view.tanHalfY, 1];
      var d = qRot(view.rotation, local), l = Math.sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
      return { origin: view.position.slice(), direction: [d[0] / l, d[1] / l, d[2] / l] };
    }

    function projectToView(view, world) {
      var local = qRot(qInv(view.rotation), [world[0] - view.position[0], world[1] - view.position[1], world[2] - view.position[2]]);
      if (local[2] <= 1e-4) return null;
      return [0.5 + local[0] / local[2] / (2 * view.tanHalfX), 0.5 - local[1] / local[2] / (2 * view.tanHalfY)];
    }

    // Plane detection, simulated: a surface becomes known after enough of it has been in view, close by, for a second.
    var detectTimer = 0;
    function detectSurfaces(dt) {
      detectTimer += dt;
      if (detectTimer < 0.2) return;
      var step = detectTimer;
      detectTimer = 0;
      var view = { position: head.position, rotation: headQuat(), tanHalfX: Math.tan(31 * DEG) * 1.45, tanHalfY: Math.tan(31 * DEG) };
      // What the centre of the view is resting on, so a surface the wearer is standing right up against counts too.
      var looks = [];
      for (var a = 0; a < 3; a++) for (var c = 0; c < 3; c++) {
        var ray = viewRay(view, 0.25 + a * 0.25, 0.25 + c * 0.25), seenAt = castReal(ray.origin, ray.direction, 6);
        if (seenAt) looks.push(seenAt.position);
      }
      planes.forEach(function (p) {
        if (p.found) return;
        var visible = 0, def = p.def, inv = qInv(def.rotation);
        for (var k = 0; k < looks.length && visible < 3; k++) {
          var l = qRot(inv, [looks[k][0] - def.position[0], looks[k][1] - def.position[1], looks[k][2] - def.position[2]]);
          var off = def.kind === 'wall' ? l[2] : (def.kind === 'table' ? l[1] - def.size[1] / 2 : l[1]);
          var across = def.kind === 'wall' ? l[1] / Math.max(def.size[1], 1e-3) : l[2] / Math.max(def.size[2], 1e-3);
          if (Math.abs(off) < 0.05 && Math.abs(l[0]) <= def.size[0] / 2 && Math.abs(across) <= 0.5) visible++;
        }
        for (var i = 0; i < p.samples.length && visible < 3; i++) {
          var s = p.samples[i], d = [s[0] - head.position[0], s[1] - head.position[1], s[2] - head.position[2]];
          var dist = Math.sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
          if (dist > 6 || dist < 0.2) continue;
          var uv = projectToView(view, s);
          if (!uv || uv[0] < 0.04 || uv[0] > 0.96 || uv[1] < 0.04 || uv[1] > 0.96) continue;
          var hit = castReal(head.position, [d[0] / dist, d[1] / dist, d[2] / dist], dist + 0.2);
          if (hit && hit.distance < dist - 0.08) continue;   // something is in the way
          visible++;
        }
        if (visible >= 3) p.seen += step; else p.seen = Math.max(0, p.seen - step * 0.5);
        if (p.seen >= 1) {
          p.found = true;
          discovered.push(p.def);
          outline(p.def);
          emit({ type: 'surface', surface: surfaceJson(p.def), count: discovered.length });
        }
      });
    }

    function outline(def) {
      var w = def.size[0], pts;
      if (def.kind === 'wall') { var h = def.size[1]; pts = [[-w / 2, -h / 2, 0.012], [w / 2, -h / 2, 0.012], [w / 2, h / 2, 0.012], [-w / 2, h / 2, 0.012]]; }
      else { var d = def.size[2], y = def.kind === 'table' ? def.size[1] / 2 + 0.008 : 0.008; pts = [[-w / 2, y, -d / 2], [w / 2, y, -d / 2], [w / 2, y, d / 2], [-w / 2, y, d / 2]]; }
      // Inset a little so neighbouring outlines do not coincide.
      var inset = 0.04, flat = [];
      pts.forEach(function (p) {
        var q = p.slice();
        if (def.kind === 'wall') { q[0] -= Math.sign(q[0]) * inset; q[1] -= Math.sign(q[1]) * inset; } else { q[0] -= Math.sign(q[0]) * inset; q[2] -= Math.sign(q[2]) * inset; }
        var world = qRot(def.rotation, q);
        flat.push(def.position[0] + world[0], def.position[1] + world[1], -(def.position[2] + world[2]));
      });
      var material = new THREE.LineDashedMaterial({ color: T.outline, dashSize: 0.12, gapSize: 0.08, transparent: true, opacity: 0.55 });
      var loop = new THREE.LineLoop(new THREE.BufferGeometry().setAttribute('position', new THREE.Float32BufferAttribute(flat, 3)), material);
      loop.computeLineDistances();
      loop.userData.outline = true;
      if (def.backdrop) backdrops.push(loop);
      helperGroup.add(loop);
    }

    function surfaceJson(def) {
      return { id: def.id, kind: def.kind, label: def.label || def.kind, position: def.position.map(round), rotation: def.rotation.map(round), size: def.size.map(round) };
    }

    function propJson(e) {
      var o = { id: e.id, kind: e.kind, position: e.position, rotation: e.rotation, size: e.size, name: e.label || e.id, origin: e.origin || e.position };
      if (e.aliases && e.aliases.length) o.aliases = e.aliases;
      if (e.about) o.about = e.about;
      if (e.parent) o.parent = e.parent;
      if (e.actionPoint) o.actionPoint = e.actionPoint;
      if (e.actionAxis) o.actionAxis = qRot(e.rotation, e.actionAxis).map(round);
      return o;
    }

    function normalize(s) { return String(s || '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim(); }

    // What space.find does when nobody has plugged in a vision service: it can only recognise stand-in props.
    function defaultFinder(description) {
      var wanted = normalize(description), best = null, bestScore = 0;
      space.props.forEach(function (p) {
        [p.label, p.id].concat(p.aliases || []).forEach(function (n) {
          var name = normalize(n), score = 0;
          if (!name) return;
          if (name === wanted) score = 1000 + name.length;
          else if ((' ' + wanted + ' ').indexOf(' ' + name + ' ') >= 0) score = 500 + name.length;
          else if ((' ' + name + ' ').indexOf(' ' + wanted + ' ') >= 0) score = 200 + wanted.length;
          if (score > bestScore) { bestScore = score; best = p; }
        });
      });
      if (!best || best.kind === 'equipment' && bestScore < 500) return Promise.resolve(best ? [propHit(best)] : []);
      return Promise.resolve([propHit(best)]);
    }
    function propHit(p) {
      var d = [p.position[0] - head.position[0], p.position[1] - head.position[1], p.position[2] - head.position[2]];
      return { position: p.position, normal: [0, 1, 0], distance: round(Math.sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2])), label: p.label || p.id };
    }
    var finder = defaultFinder;

    // ---- content: nodes created by programs ------------------------------------------------------

    var nodes = {}, nextNode = 1, billboards = [], flowMaterials = [];
    var GEOMETRY_KEYS = { size: 1, radius: 1, height: 1, topRadius: 1, tube: 1, arc: 1, points: 1, flow: 1, positions: 1, indices: 1, colors: 1, text: 1, weight: 1, align: 1, anchor: 1, maxWidth: 1, width: 1, borderWidth: 1, shade: 1, glow: 1 };

    function solidMaterial(props) {
      var colour = new THREE.Color(props.color || '#6C5CE7');
      var opacity = props.opacity === undefined ? 1 : props.opacity;
      var material;
      if (props.glow) material = new THREE.MeshBasicMaterial({ color: colour, transparent: true, opacity: opacity, depthWrite: false });
      else if (props.shade === false) material = new THREE.MeshBasicMaterial({ color: colour, transparent: opacity < 1, opacity: opacity, depthWrite: opacity >= 1 });
      else material = new THREE.MeshLambertMaterial({ color: colour, transparent: opacity < 1, opacity: opacity, depthWrite: opacity >= 0.5 });
      return material;
    }

    var FLOW_VERTEX = 'varying vec2 vUv; void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }';
    var FLOW_FRAGMENT = [
      'uniform vec3 uColor; uniform float uOpacity; uniform float uBase; uniform float uDensity; uniform float uSpeed; uniform float uTime; varying vec2 vUv;',
      'void main() {',
      '  float fold = abs(fract(vUv.y * 2.0) * 2.0 - 1.0);',
      '  float t = fract((vUv.x - uTime * uSpeed) * uDensity + fold * 0.45);',
      '  float band = smoothstep(0.0, 0.1, t) * (1.0 - smoothstep(0.4, 0.5, t));',
      '  gl_FragColor = vec4(uColor, uOpacity * mix(uBase, 1.0, band));',
      '}'].join('\n');

    function disposeVisual(node) {
      node.meshes.forEach(function (m) {
        node.obj.remove(m);
        if (m.geometry) m.geometry.dispose();
        if (m.material) {
          if (m.material.map) m.material.map.dispose();
          var i = flowMaterials.indexOf(m.material);
          if (i >= 0) flowMaterials.splice(i, 1);
          m.material.dispose();
        }
      });
      node.meshes = [];
      node.fill = node.border = node.main = null;
    }

    function addMesh(node, mesh) {
      mesh.userData.nodeId = node.id;
      node.obj.add(mesh);
      node.meshes.push(mesh);
      return mesh;
    }

    function buildVisual(node) {
      disposeVisual(node);
      var p = node.props, kind = node.kind, geometry = null;
      if (kind === 'group') return;
      if (kind === 'text') { buildText(node); return; }
      if (kind === 'panel') { buildPanel(node); return; }

      if (kind === 'box') { var s = p.size || [0.1, 0.1, 0.1]; geometry = new THREE.BoxGeometry(s[0], s[1], s[2]); }
      else if (kind === 'sphere') geometry = new THREE.SphereGeometry(p.radius || 0.05, 28, 18);
      else if (kind === 'cylinder') geometry = new THREE.CylinderGeometry(p.topRadius !== undefined ? p.topRadius : (p.radius || 0.05), p.radius || 0.05, p.height || 0.1, 32);
      else if (kind === 'torus') geometry = torusGeometry(p.radius || 0.1, p.tube || 0.01, p.arc === undefined ? 360 : p.arc, 48, 12);
      else if (kind === 'disc') { geometry = new THREE.CircleGeometry(p.radius || 0.1, 48); geometry.rotateX(-Math.PI / 2); }
      else if (kind === 'tube') {
        var pts = p.points || [];
        if (pts.length < 2) return;
        geometry = tubeGeometry(pts, p.radius || 0.01, 20, p.flow ? toV3(head.position) : null);
      } else if (kind === 'mesh') {
        var colors = null;
        if (p.colors && p.colors.length) {
          colors = [];
          p.colors.forEach(function (hex) { var c = new THREE.Color(hex); colors.push(c.r, c.g, c.b); });
        }
        geometry = unityGeometry(p.positions || [], p.indices || [], null, colors);
      } else return;

      var material;
      if (kind === 'tube' && p.flow) {
        var flow = typeof p.flow === 'object' ? p.flow : {};
        material = new THREE.ShaderMaterial({
          uniforms: {
            uColor: { value: new THREE.Color(p.color || '#6C5CE7') }, uOpacity: { value: p.opacity === undefined ? 1 : p.opacity }, uBase: { value: 0.18 },
            uDensity: { value: flow.density || 7 }, uSpeed: { value: flow.speed === undefined ? 0.35 : flow.speed }, uTime: { value: 0 }
          },
          vertexShader: FLOW_VERTEX, fragmentShader: FLOW_FRAGMENT, transparent: true, depthWrite: false, side: THREE.FrontSide
        });
        flowMaterials.push(material);
      } else {
        material = solidMaterial(p);
        if (kind === 'disc' || kind === 'mesh') material.side = THREE.DoubleSide;
        if (kind === 'mesh' && p.colors && p.colors.length) { material.vertexColors = true; material.color.set('#FFFFFF'); }
      }
      node.main = addMesh(node, new THREE.Mesh(geometry, material));
      if (p.glow || (kind === 'tube' && p.flow)) node.main.renderOrder = 3;
    }

    function buildPanel(node) {
      var p = node.props, w = Math.max(1e-4, p.width || 0.1), h = Math.max(1e-4, p.height || 0.05);
      var radius = Math.min(p.radius === undefined ? 0.02 : p.radius, w / 2, h / 2);
      var opacity = p.opacity === undefined ? 1 : p.opacity;
      var fill = new THREE.Mesh(new THREE.ShapeGeometry(roundedRectShape(w, h, radius), 10),
        new THREE.MeshBasicMaterial({ color: p.fill || '#FFFFFF', transparent: opacity < 1, opacity: opacity, depthWrite: opacity > 0.02, side: THREE.DoubleSide }));
      node.fill = addMesh(node, fill);
      var bw = p.border ? (p.borderWidth === undefined ? 0.0026 : p.borderWidth) : 0;
      if (bw > 0 && Math.min(w, h) > bw * 2) {
        var ring = roundedRectShape(w, h, radius);
        ring.holes.push(roundedRectShape(w - bw * 2, h - bw * 2, Math.max(0, radius - bw)));
        var border = new THREE.Mesh(new THREE.ShapeGeometry(ring, 10),
          new THREE.MeshBasicMaterial({ color: p.border, transparent: opacity < 1, opacity: opacity, depthWrite: false, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1 }));
        border.position.z = 0.0003;
        node.border = addMesh(node, border);
      }
    }

    function buildText(node) {
      var p = node.props, size = p.size || 0.022, weight = p.weight === 'semibold' ? 'semibold' : 'medium';
      var block = measureBlock(data.fonts, p.text === undefined ? '' : String(p.text), size, weight, p.maxWidth || 0);
      var font = block.font, lines = block.lines;
      if (!lines.length || block.width <= 0) return;
      var align = p.align || 'left', anchor = p.anchor || 'middle';
      var px = clamp(Math.round(size * 2600), 30, 120);          // canvas pixels per em
      var ppm = px / size;
      if (block.width * ppm > 3800) { ppm = 3800 / block.width; px = ppm * size; }
      var pad = Math.ceil(px * 0.18);
      var cap = font.capHeight * size, lineStep = font.lineHeight * size;
      var above = (font.ascender - font.capHeight) * size, below = font.descender * size;
      var cw = Math.ceil(block.width * ppm) + pad * 2, ch = Math.ceil((block.height + above + below) * ppm) + pad * 2;
      var c = document.createElement('canvas');
      c.width = cw; c.height = ch;
      var ctx = c.getContext('2d');
      ctx.font = (weight === 'semibold' ? '600 ' : '500 ') + px + 'px ' + FONT_STACK;
      ctx.textBaseline = 'alphabetic';
      ctx.fillStyle = '#FFFFFF';
      for (var i = 0; i < lines.length; i++) {
        var line = lines[i];
        if (!line.length) continue;
        var target = widthEm(font, line) * size * ppm, natural = ctx.measureText(line).width || 1;
        var x = pad + (align === 'center' ? (block.width * ppm - target) / 2 : (align === 'right' ? block.width * ppm - target : 0));
        var y = pad + (above + cap + i * lineStep) * ppm;
        // Glyph shapes come from whatever font the browser has; the width always comes from the shared metrics.
        ctx.save();
        ctx.translate(x, y);
        ctx.scale(target / natural, 1);
        ctx.fillText(line, 0, 0);
        ctx.restore();
      }
      var texture = new THREE.CanvasTexture(c);
      texture.anisotropy = Math.min(8, renderer.capabilities.getMaxAnisotropy());
      texture.minFilter = THREE.LinearMipmapLinearFilter;
      var opacity = p.opacity === undefined ? 1 : p.opacity;
      var material = new THREE.MeshBasicMaterial({ map: texture, color: p.color || '#17161D', transparent: true, opacity: opacity, depthWrite: false, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 });
      var wM = cw / ppm, hM = ch / ppm;
      var mesh = new THREE.Mesh(new THREE.PlaneGeometry(wM, hM), material);
      var left = align === 'center' ? -block.width / 2 : (align === 'right' ? -block.width : 0);
      var top = anchor === 'top' ? 0 : (anchor === 'bottom' ? block.height : block.height / 2);
      mesh.position.set(left - pad / ppm + wM / 2, top + above + pad / ppm - hM / 2, 0);
      mesh.renderOrder = 2;
      node.main = addMesh(node, mesh);
    }

    function applyAppearance(node) {
      var p = node.props, opacity = p.opacity === undefined ? 1 : p.opacity;
      node.obj.visible = p.visible !== false;
      if (node.kind === 'panel') {
        if (node.fill) { node.fill.material.color.set(p.fill || '#FFFFFF'); node.fill.material.opacity = opacity; node.fill.material.transparent = opacity < 1; node.fill.material.depthWrite = opacity > 0.02; }
        if (node.border) { node.border.material.color.set(p.border || '#6C5CE7'); node.border.material.opacity = opacity; node.border.material.transparent = opacity < 1; }
        return;
      }
      if (!node.main) return;
      var m = node.main.material;
      if (m.uniforms) { m.uniforms.uColor.value.set(p.color || '#6C5CE7'); m.uniforms.uOpacity.value = opacity; return; }
      if (node.kind === 'text') { m.color.set(p.color || '#17161D'); m.opacity = opacity; return; }
      if (!(node.kind === 'mesh' && p.colors && p.colors.length)) m.color.set(p.color || '#6C5CE7');
      m.opacity = opacity;
      if (!p.glow) {
        var transparent = opacity < 1;
        if (m.transparent !== transparent) { m.transparent = transparent; m.needsUpdate = true; }
        m.depthWrite = opacity >= (p.shade === false ? 1 : 0.5);
      }
    }

    function setParent(node, parentId) {
      var parent = parentId ? nodes[parentId] : null;
      (parent ? parent.obj : contentGroup).add(node.obj);
      node.parent = parent ? parent.id : 0;
    }

    function setBillboard(node) {
      var i = billboards.indexOf(node), wanted = node.props.billboard === 'full' || node.props.billboard === 'upright';
      if (wanted && i < 0) billboards.push(node);
      if (!wanted && i >= 0) billboards.splice(i, 1);
    }

    function isInteractive(node) {
      for (var n = node, guard = 0; n && guard < 64; guard++, n = n.parent ? nodes[n.parent] : null) if (n.props.interactive) return n;
      return null;
    }
    function isShown(node) {
      for (var n = node, guard = 0; n && guard < 64; guard++, n = n.parent ? nodes[n.parent] : null) if (n.props.visible === false) return false;
      return true;
    }

    // ---- experiences -----------------------------------------------------------------------------

    var experiences = [], savedState = {}, clock = 0;

    function getExperience(id) {
      for (var i = 0; i < experiences.length; i++) if (experiences[i].id === id) return experiences[i];
      return null;
    }

    function addLog(exp, level, message) {
      exp.log.push((level === 'error' ? '! ' : '') + message);
      if (exp.log.length > 200) exp.log.splice(0, exp.log.length - 200);
      emit({ type: 'log', id: exp.id, level: level, message: message });
    }

    function makeHost(exp) {
      return {
        create: function (kind, json) {
          if (exp.nodes.length >= LIMITS.maxNodes) throw new Error('This experience has created too many nodes (' + LIMITS.maxNodes + ').');
          var props = json ? JSON.parse(json) : {};
          var node = { id: nextNode++, kind: String(kind), props: props, exp: exp, obj: new THREE.Group(), meshes: [], parent: 0, main: null };
          nodes[node.id] = node;
          exp.nodes.push(node.id);
          setParent(node, props.parent);
          buildVisual(node);
          applyAppearance(node);
          setBillboard(node);
          return node.id;
        },
        update: function (id, json) {
          var node = nodes[id];
          if (!node || node.exp !== exp) return;
          var changed = json ? JSON.parse(json) : {}, rebuild = false;
          Object.keys(changed).forEach(function (k) {
            node.props[k] = changed[k];
            if (GEOMETRY_KEYS[k] || (node.kind === 'panel' && k === 'border' && !node.border)) rebuild = true;
          });
          if (changed.parent !== undefined) setParent(node, changed.parent);
          if (rebuild) buildVisual(node);
          applyAppearance(node);
          if (changed.billboard !== undefined) setBillboard(node);
        },
        xf: function (id, px, py, pz, qx, qy, qz, qw, sx, sy, sz) {
          var node = nodes[id];
          if (!node || node.exp !== exp) return;
          node.obj.position.set(px, py, -pz);
          node.obj.quaternion.set(-qx, -qy, qz, qw === undefined ? 1 : qw);
          node.obj.scale.set(sx === undefined ? 1 : sx, sy === undefined ? 1 : sy, sz === undefined ? 1 : sz);
        },
        destroy: function (id) {
          var node = nodes[id];
          if (!node || node.exp !== exp) return;
          removeNode(node);
          var i = exp.nodes.indexOf(id);
          if (i >= 0) exp.nodes.splice(i, 1);
        },
        measure: function (text, size, weight, maxWidth) {
          var b = measureBlock(data.fonts, text, size || 0.022, weight, maxWidth || 0);
          return [b.width, b.height];
        },
        raycast: function (ox, oy, oz, dx, dy, dz, max) {
          if (dx * dx + dy * dy + dz * dz < 1e-10) return null;
          var hit = castReal([ox, oy, oz], [dx, dy, dz], max === undefined ? 10 : max);
          return hit ? JSON.stringify(hit) : null;
        },
        hasView: function () { return !!exp.view; },
        viewRay: function (u, v) { return exp.view ? JSON.stringify(viewRay(exp.view, +u || 0, +v || 0)) : null; },
        floorY: function () { return data.room.floorY || 0; },
        surfaces: function () { return JSON.stringify(discovered.map(surfaceJson)); },
        props: function () { return JSON.stringify(space.props.map(propJson)); },
        user: function () {
          var q = headQuat(), f = headForward(), p = head.position;
          return [round(p[0]), round(p[1]), round(p[2]), round(f[0]), round(f[1]), round(f[2]), round(q[0]), round(q[1]), round(q[2]), round(q[3])];
        },
        find: function (query, callback) {
          var done = function (hits) {
            if (!exp.running) return;
            guarded(exp, function () { exp.scope.__resolve(callback, JSON.stringify(hits || [])); });
          };
          var result;
          try { result = finder(String(query), { view: exp.view, id: exp.id }); } catch (e) { result = []; }
          // Always answer later, as a device does after asking a vision service.
          Promise.resolve(result).then(function (hits) { setTimeout(function () { done(hits); }, 120); }, function () { setTimeout(function () { done([]); }, 120); });
        },
        log: function (level, message) {
          if (level === 'error' && ++exp.errorCount > LIMITS.maxErrors) throw new Error('Stopped after ' + LIMITS.maxErrors + ' errors. Last: ' + message);
          addLog(exp, String(level), String(message));
        },
        fail: function (message) { exp.pendingError = String(message); addLog(exp, 'error', exp.pendingError); },
        finish: function () { exp.finishRequested = true; }
      };
    }

    function removeNode(node) {
      disposeVisual(node);
      if (node.obj.parent) node.obj.parent.remove(node.obj);
      var i = billboards.indexOf(node);
      if (i >= 0) billboards.splice(i, 1);
      if (hovered && hovered.id === node.id) hovered = null;
      delete nodes[node.id];
    }

    function teardown(exp) {
      exp.running = false;
      exp.nodes.slice().forEach(function (id) { if (nodes[id]) removeNode(nodes[id]); });
      exp.nodes = [];
      exp.scope = null;
    }

    function fail(exp, message) {
      if (exp.error !== message && exp.pendingError !== message) addLog(exp, 'error', message);
      exp.error = message;
      teardown(exp);
      var i = experiences.indexOf(exp);
      if (i >= 0) experiences.splice(i, 1);
      delete savedState[exp.id];
      emit({ type: 'failed', id: exp.id, title: exp.title, error: message });
    }

    // Runs a host-to-program call; a throw here means the library itself gave up, so the experience stops.
    function guarded(exp, fn) {
      try { return fn(); }
      catch (e) { fail(exp, describe(exp, e)); return undefined; }
    }
    function describe(exp, e) {
      if (exp.scope && exp.scope.__describe) { try { return exp.scope.__describe(e); } catch (x) { /* fall through */ } }
      return e && e.message !== undefined ? (e.name || 'Error') + ': ' + e.message : String(e);
    }

    function stop(id, keepState) {
      var exp = getExperience(id);
      if (!exp) return false;
      if (keepState && exp.scope && !exp.error) { try { savedState[id] = exp.scope.__saveState(); } catch (e) { delete savedState[id]; } }
      else if (!keepState) delete savedState[id];
      teardown(exp);
      experiences.splice(experiences.indexOf(exp), 1);
      emit({ type: 'stopped', id: exp.id, title: exp.title });
      return true;
    }

    function run(spec) {
      spec = spec || {};
      var id = spec.id || ('experience-' + (experiences.length + 1));
      stop(id, true);
      var exp = { id: id, title: spec.title || 'Experience', prompt: spec.prompt || '', code: String(spec.code || ''), view: spec.view || null, running: true, error: null, log: [], nodes: [], errorCount: 0, scope: null, finishRequested: false, pendingError: null };
      experiences.push(exp);
      try {
        var scope = { __host: makeHost(exp) };
        if (savedState[id] !== undefined) scope.__initialState = savedState[id];
        exp.scope = scope;
        // The library is evaluated once per experience with its own global object, as each device engine does.
        new Function('globalThis', librarySource)(scope);
        // Programs get the Spatail API and nothing else by name: the browser's own globals are shadowed, as they
        // do not exist in the engine the devices use.
        var args = API.concat(SHADOWED).concat(['__self']);
        var body = 'return (async function () {\n' + exp.code + '\n}).call(__self);';
        var program = Function.apply(null, args.concat([body]));
        var values = API.map(function (name) { return scope[name]; });
        for (var k = 0; k < SHADOWED.length; k++) values.push(undefined);
        values.push({});
        var promise = program.apply({}, values);
        promise.catch(function (e) {
          if (!exp.running) return;
          fail(exp, describe(exp, e));
        });
      } catch (e) {
        fail(exp, describe(exp, e));
        return { id: id, error: exp.error, settled: Promise.resolve({ id: id, error: exp.error }) };
      }
      if (exp.pendingError) { fail(exp, exp.pendingError); return { id: id, error: exp.error, settled: Promise.resolve({ id: id, error: exp.error }) }; }
      emit({ type: 'started', id: id, title: exp.title });
      // A browser cannot run promise continuations synchronously the way the device engine does, so an error
      // thrown before the program's first real wait is only known a moment later. `settled` resolves then.
      var settled = new Promise(function (resolve) { setTimeout(function () { resolve(exp.error ? { id: id, error: exp.error } : { id: id }); }, 30); });
      return { id: id, settled: settled };
    }

    function tickExperiences(dt) {
      clock += dt;
      for (var i = experiences.length - 1; i >= 0; i--) {
        var exp = experiences[i];
        if (!exp.running) continue;
        guarded(exp, function () { exp.scope.__tick(dt, clock); });
        if (!exp.running) continue;
        if (exp.pendingError) fail(exp, exp.pendingError);
        else if (exp.finishRequested) stop(exp.id, false);
      }
    }

    function dispatch(node, type) {
      var exp = node.exp;
      if (!exp || !exp.running) return false;
      var handled = guarded(exp, function () { return exp.scope.__event(node.id, type); });
      return handled === true;
    }

    // ---- pointer: look, orbit, click -----------------------------------------------------------------

    var pointers = {}, pointerCount = 0, moved = 0, hovered = null, keys = {}, pinch = 0;

    function pickContent(clientX, clientY) {
      var rect = canvas.getBoundingClientRect();
      var ndc = new THREE.Vector2(((clientX - rect.left) / rect.width) * 2 - 1, -((clientY - rect.top) / rect.height) * 2 + 1);
      raycaster.setFromCamera(ndc, activeCamera());
      raycaster.near = 0; raycaster.far = 60;
      var meshes = [];
      Object.keys(nodes).forEach(function (id) { var n = nodes[id]; if (n.meshes.length && isShown(n)) n.meshes.forEach(function (m) { meshes.push(m); }); });
      var hits = raycaster.intersectObjects(meshes, false);
      for (var i = 0; i < hits.length; i++) {
        var node = nodes[hits[i].object.userData.nodeId];
        if (node && isInteractive(node)) return node;
      }
      return null;
    }

    function onPointerDown(e) {
      canvas.focus({ preventScroll: true });
      try { canvas.setPointerCapture(e.pointerId); } catch (x) { /* older browsers */ }
      pointers[e.pointerId] = { x: e.clientX, y: e.clientY };
      pointerCount = Object.keys(pointers).length;
      moved = 0;
      pinch = 0;
    }
    function onPointerMove(e) {
      var p = pointers[e.pointerId];
      if (!p) { hover(e); return; }
      var dx = e.clientX - p.x, dy = e.clientY - p.y;
      p.x = e.clientX; p.y = e.clientY;
      moved += Math.abs(dx) + Math.abs(dy);
      if (pointerCount >= 2) {
        // Two fingers: walk (first person) or zoom and pan (third person).
        var ids = Object.keys(pointers), a = pointers[ids[0]], b = pointers[ids[1]], d = Math.hypot(a.x - b.x, a.y - b.y);
        if (mode === 'first') walk(-dy * 0.006, dx * 0.006);
        else { if (pinch) orbit.distance = clamp(orbit.distance * pinch / Math.max(1, d), 1.5, 30); pinch = d; }
        return;
      }
      if (mode === 'first') { head.yaw -= dx * 0.16; head.pitch = clamp(head.pitch - dy * 0.16, -75, 75); }
      else { orbit.yaw -= dx * 0.3; orbit.pitch = clamp(orbit.pitch + dy * 0.25, 4, 85); }
    }
    function onPointerUp(e) {
      var wasTracked = !!pointers[e.pointerId];
      delete pointers[e.pointerId];
      pointerCount = Object.keys(pointers).length;
      if (wasTracked && pointerCount === 0 && moved < 6) {
        var node = pickContent(e.clientX, e.clientY);
        if (node) dispatch(node, 'click');
      }
    }
    function hover(e) {
      var node = pickContent(e.clientX, e.clientY);
      var root = node ? isInteractive(node) : null;
      if ((root && hovered && root.id === hovered.id) || (!root && !hovered)) return;
      if (hovered && nodes[hovered.id]) dispatch(hovered, 'exit');
      hovered = root;
      if (hovered) dispatch(hovered, 'enter');
      canvas.style.cursor = hovered ? 'pointer' : (mode === 'first' ? 'grab' : 'move');
    }
    function onWheel(e) {
      if (mode !== 'orbit' || document.activeElement !== canvas) return;
      e.preventDefault();
      orbit.distance = clamp(orbit.distance * Math.exp(e.deltaY * 0.0012), 1.5, 30);
    }
    var KEYMAP = { w: 'f', arrowup: 'f', s: 'b', arrowdown: 'b', a: 'l', d: 'r', arrowleft: 'tl', arrowright: 'tr', q: 'tl', e: 'tr' };
    function onKeyDown(e) { var k = KEYMAP[String(e.key).toLowerCase()]; if (k) { keys[k] = true; e.preventDefault(); } }
    function onKeyUp(e) { var k = KEYMAP[String(e.key).toLowerCase()]; if (k) keys[k] = false; }
    function onBlur() { keys = {}; }

    canvas.addEventListener('pointerdown', onPointerDown);
    canvas.addEventListener('pointermove', onPointerMove);
    canvas.addEventListener('pointerup', onPointerUp);
    canvas.addEventListener('pointercancel', onPointerUp);
    canvas.addEventListener('wheel', onWheel, { passive: false });
    canvas.addEventListener('keydown', onKeyDown);
    canvas.addEventListener('keyup', onKeyUp);
    canvas.addEventListener('blur', onBlur);
    canvas.style.cursor = 'grab';

    function walk(forward, right) {
      var f = qRot(yawQuat(head.yaw), [0, 0, 1]), r = qRot(yawQuat(head.yaw), [1, 0, 0]);
      head.position[0] = clamp(head.position[0] + f[0] * forward + r[0] * right, -bounds[0], bounds[0]);
      head.position[2] = clamp(head.position[2] + f[2] * forward + r[2] * right, bounds[2], bounds[3]);
    }

    function applyKeys(dt) {
      var speed = 1.6 * dt;
      if (keys.f) walk(speed, 0);
      if (keys.b) walk(-speed, 0);
      if (keys.l) walk(0, -speed);
      if (keys.r) walk(0, speed);
      if (keys.tl) head.yaw -= 70 * dt;
      if (keys.tr) head.yaw += 70 * dt;
    }

    // ---- cameras, marker ---------------------------------------------------------------------------

    var marker = null;
    function buildMarker() {
      marker = new THREE.Group();
      var body = new THREE.Mesh(new THREE.BoxGeometry(0.2, 0.1, 0.12), new THREE.MeshLambertMaterial({ color: T.marker }));
      marker.add(body);
      // View cone, looking along -Z in Three (the camera's own convention).
      var d = 0.9, hx = d * Math.tan(31 * DEG) * 1.3, hy = d * Math.tan(31 * DEG);
      var pts = [0, 0, 0, -hx, -hy, -d, 0, 0, 0, hx, -hy, -d, 0, 0, 0, hx, hy, -d, 0, 0, 0, -hx, hy, -d, -hx, -hy, -d, hx, -hy, -d, hx, -hy, -d, hx, hy, -d, hx, hy, -d, -hx, hy, -d, -hx, hy, -d, -hx, -hy, -d];
      marker.add(new THREE.LineSegments(new THREE.BufferGeometry().setAttribute('position', new THREE.Float32BufferAttribute(pts, 3)), new THREE.LineBasicMaterial({ color: T.marker, transparent: true, opacity: 0.6 })));
      var stem = new THREE.Mesh(new THREE.CylinderGeometry(0.008, 0.008, 1, 8), new THREE.MeshBasicMaterial({ color: T.marker, transparent: true, opacity: 0.35 }));
      stem.userData.stem = true;
      marker.add(stem);
      helperGroup.add(marker);
    }

    function placeCameras() {
      var q = headQuat();
      userCam.position.copy(toV3(head.position));
      userCam.quaternion.copy(toQuat(q));
      userCam.updateMatrixWorld(true);
      var yaw = orbit.yaw * DEG, pitch = orbit.pitch * DEG;
      orbitCam.position.set(orbit.target.x + orbit.distance * Math.sin(yaw) * Math.cos(pitch), orbit.target.y + orbit.distance * Math.sin(pitch), orbit.target.z + orbit.distance * Math.cos(yaw) * Math.cos(pitch));
      orbitCam.lookAt(orbit.target);
      orbitCam.updateMatrixWorld(true);
      for (var b = 0; b < backdrops.length; b++) backdrops[b].visible = mode !== 'orbit';
      if (marker) {
        marker.visible = mode === 'orbit';
        marker.position.copy(userCam.position);
        marker.quaternion.copy(userCam.quaternion);
        marker.children.forEach(function (c) {
          if (!c.userData.stem) return;
          // Keep the stem vertical, down to the floor, whatever the head is doing.
          c.quaternion.copy(marker.quaternion).invert();
          c.scale.y = head.position[1];
          c.position.copy(new THREE.Vector3(0, -head.position[1] / 2, 0).applyQuaternion(c.quaternion));
        });
      }
    }
    function activeCamera() { return mode === 'orbit' ? orbitCam : userCam; }

    var bbTarget = new THREE.Vector3(), bbWorld = new THREE.Vector3();
    function faceBillboards() {
      if (!billboards.length) return;
      contentGroup.updateMatrixWorld(true);
      billboards.forEach(function (node) {
        if (!isShown(node)) return;
        node.obj.getWorldPosition(bbWorld);
        bbTarget.copy(userCam.position);
        if (node.props.billboard === 'upright') bbTarget.y = bbWorld.y;
        if (bbTarget.distanceToSquared(bbWorld) < 1e-8) return;
        // Content reads from its -Z side in Unity, which is +Z here; lookAt points +Z at the target.
        node.obj.lookAt(bbTarget);
        node.obj.updateMatrixWorld(true);
      });
    }

    // ---- frame loop ---------------------------------------------------------------------------------

    var lastTime = 0, raf = 0, onScreen = true, width = 0, height = 0;

    function resize() {
      var w = Math.max(1, container.clientWidth), h = Math.max(1, container.clientHeight);
      if (w === width && h === height) return;
      width = w; height = h;
      renderer.setSize(w, h, false);
      userCam.aspect = orbitCam.aspect = w / h;
      // Keep a useful horizontal field of view on tall phone screens.
      userCam.fov = w / h < 1 ? 74 : (w / h > 2.2 ? 40 : 52);
      userCam.updateProjectionMatrix();
      orbitCam.updateProjectionMatrix();
    }

    function frame(now) {
      raf = 0;
      if (disposed) return;
      var dt = lastTime ? Math.min(0.1, (now - lastTime) / 1000) : 1 / 60;
      lastTime = now;
      step(dt);
      schedule();
    }

    function step(dt) {
      resize();
      applyKeys(dt);
      placeCameras();
      detectSurfaces(dt);
      tickExperiences(dt);
      flowMaterials.forEach(function (m) { m.uniforms.uTime.value = clock; });
      faceBillboards();
      renderer.render(scene3, activeCamera());
    }

    function schedule() {
      if (raf || disposed || !onScreen || document.hidden) return;
      raf = requestAnimationFrame(frame);
    }

    var io = null, ro = null;
    if ('IntersectionObserver' in window) {
      io = new IntersectionObserver(function (entries) { onScreen = entries[0].isIntersecting; lastTime = 0; schedule(); });
      io.observe(container);
    }
    if ('ResizeObserver' in window) { ro = new ResizeObserver(function () { resize(); }); ro.observe(container); }
    function onVisibility() { lastTime = 0; schedule(); }
    document.addEventListener('visibilitychange', onVisibility);

    // ---- capture: what the device camera would hand to the planner -----------------------------------

    function capture() {
      if (!captureTarget) captureTarget = new THREE.WebGLRenderTarget(CAPTURE_W, CAPTURE_H);
      var q = headQuat();
      captureCam.position.copy(toV3(head.position));
      captureCam.quaternion.copy(toQuat(q));
      captureCam.updateMatrixWorld(true);
      // The camera sees the world, not the holograms the app is drawing on top of it. Stand-in props are part
      // of this lab's world, so they stay.
      var contentWas = contentGroup.visible, helperWas = helperGroup.visible, background = scene3.background;
      contentGroup.visible = false;
      helperGroup.visible = false;
      backdrops.forEach(function (b) { b.visible = true; });
      scene3.background = new THREE.Color(T.bgBottom);
      renderer.setRenderTarget(captureTarget);
      renderer.render(scene3, captureCam);
      var pixels = new Uint8Array(CAPTURE_W * CAPTURE_H * 4);
      renderer.readRenderTargetPixels(captureTarget, 0, 0, CAPTURE_W, CAPTURE_H, pixels);
      renderer.setRenderTarget(null);
      scene3.background = background;
      contentGroup.visible = contentWas;
      helperGroup.visible = helperWas;

      var c = document.createElement('canvas');
      c.width = CAPTURE_W; c.height = CAPTURE_H;
      var ctx = c.getContext('2d'), image = ctx.createImageData(CAPTURE_W, CAPTURE_H), row = CAPTURE_W * 4;
      // WebGL rows run bottom to top.
      for (var y = 0; y < CAPTURE_H; y++) image.data.set(pixels.subarray((CAPTURE_H - 1 - y) * row, (CAPTURE_H - y) * row), y * row);
      ctx.putImageData(image, 0, 0);

      var tanY = Math.tan(captureCam.fov * DEG / 2);
      var view = { position: head.position.map(round), rotation: q.map(round), tanHalfX: round(tanY * captureCam.aspect), tanHalfY: round(tanY), width: CAPTURE_W, height: CAPTURE_H };
      var metres = [], columns = 8, rows = 6;
      for (var r = 0; r < rows; r++) for (var col = 0; col < columns; col++) {
        var ray = viewRay(view, (col + 0.5) / columns, (r + 0.5) / rows), hit = castReal(ray.origin, ray.direction, 15);
        metres.push(hit ? Math.round(hit.distance * 100) / 100 : -1);
      }
      last = { view: view, imageDataUrl: c.toDataURL('image/jpeg', 0.82), depth: { columns: columns, rows: rows, metres: metres } };
      return last;
    }
    var last = null;

    // The JSON body for POST /v1/build, in the same shape the devices send (BuildRequest.ToJson).
    function buildRequest(prompt, repair) {
      var shot = capture(), f = headForward();
      var body = {
        prompt: String(prompt || ''),
        view: { width: shot.view.width, height: shot.view.height, depth: shot.depth },
        image: shot.imageDataUrl.replace(/^data:image\/jpeg;base64,/, ''),
        user: { position: head.position.map(round), forward: f.map(round) },
        floorY: data.room.floorY || 0,
        surfaces: discovered.map(function (d) {
          var facing = qRot(d.rotation, d.kind === 'wall' ? [0, 0, 1] : [0, 1, 0]);
          return { id: d.id, kind: d.kind, position: d.position.map(round), size: d.size.map(round), facing: facing.map(round) };
        }),
        props: space.props.map(function (e) {
          var p = { id: e.id, name: e.label || e.id, position: e.position, size: e.size };
          if (e.aliases && e.aliases.length) p.aliases = e.aliases;
          if (e.about) p.about = e.about;
          if (e.parent) p.partOf = e.parent;
          var uv = projectToView(shot.view, e.position);
          if (uv && uv[0] >= 0 && uv[0] <= 1 && uv[1] >= 0 && uv[1] <= 1) p.inView = [Math.round(uv[0] * 1000) / 1000, Math.round(uv[1] * 1000) / 1000];
          return p;
        }),
        running: experiences.map(function (e) { return { id: e.id, title: e.title, prompt: e.prompt, code: e.code }; })
      };
      if (repair && repair.error) body.repair = { code: repair.code || '', error: repair.error };
      return body;
    }

    // ---- theme, space, lifecycle ---------------------------------------------------------------------

    function applyTheme() {
      T = THEMES[themeName];
      if (options.background !== false) container.style.background = 'linear-gradient(180deg, ' + T.bgTop + ' 0%, ' + T.bgBottom + ' 100%)';
      hemi.color.set(T.hemiSky); hemi.groundColor.set(T.hemiGround); hemi.intensity = T.hemi; sun.intensity = T.sun;
    }

    function setSpace(name, seed) {
      var def = null;
      SPACES.forEach(function (s) { if (s.name.toLowerCase() === String(name || '').toLowerCase()) def = s; });
      if (!def) def = SPACES[0];
      experiences.slice().forEach(function (e) { stop(e.id, false); });
      savedState = {};
      spaceName = def.name;
      spaceSeed = def.seeded ? (seed === undefined || seed === null ? Math.floor(Math.random() * 100000) + 1 : (seed | 0)) : 0;
      space = def.make(spaceSeed, data);
      head = { position: space.user.position.slice(), yaw: space.user.yaw, pitch: space.user.pitch };
      bounds = space.bounds;
      orbit.target.set(space.orbit[0], space.orbit[1], -space.orbit[2]);
      orbit.distance = spaceName === 'Street' ? 13 : 9.5;
      orbit.yaw = 25; orbit.pitch = 28;
      last = null;
      buildSpace();
      emit({ type: 'space', name: spaceName, seed: spaceSeed });
      return { name: spaceName, seed: spaceSeed };
    }

    function setTheme(name) {
      themeName = name === 'dark' ? 'dark' : 'light';
      applyTheme();
      // Rebuild the world in the new palette, keeping what has been discovered and what is running.
      var found = planes.filter(function (p) { return p.found; }).map(function (p) { return p.def.id; });
      buildSpace();
      planes.forEach(function (p) { if (found.indexOf(p.def.id) >= 0) { p.found = true; p.seen = 1; discovered.push(p.def); outline(p.def); } });
    }

    function dispose() {
      if (disposed) return;
      disposed = true;
      if (raf) cancelAnimationFrame(raf);
      experiences.slice().forEach(function (e) { stop(e.id, false); });
      document.removeEventListener('visibilitychange', onVisibility);
      if (io) io.disconnect();
      if (ro) ro.disconnect();
      clearGroup(realGroup); clearGroup(helperGroup);
      if (captureTarget) captureTarget.dispose();
      renderer.dispose();
      if (canvas.parentNode) canvas.parentNode.removeChild(canvas);
      listeners = [];
    }

    // Text drawn before the web font arrived used a fallback face; redraw it once the real one is in.
    if (document.fonts && document.fonts.ready) {
      document.fonts.ready.then(function () {
        if (disposed) return;
        Object.keys(nodes).forEach(function (id) { var n = nodes[id]; if (n.kind === 'text') { buildVisual(n); applyAppearance(n); } });
      });
    }

    applyTheme();
    setSpace(options.space || 'Workshop', options.seed);
    resize();
    schedule();

    return {
      canvas: canvas,
      run: run,
      stop: function (id) { return stop(id, false); },
      stopAll: function () { experiences.slice().forEach(function (e) { stop(e.id, false); }); },
      experiences: function () {
        return experiences.map(function (e) { return { id: e.id, title: e.title, prompt: e.prompt, code: e.code, error: e.error, nodeCount: e.nodes.length, log: e.log.slice() }; });
      },
      say: function (text) {
        for (var i = experiences.length - 1; i >= 0; i--) {
          var exp = experiences[i];
          if (!exp.running) continue;
          var handled = guarded(exp, function () { return exp.scope.__voice(String(text || '')); });
          if (handled === true) return true;
        }
        return false;
      },
      setSpace: setSpace,
      spaces: function () { return SPACES.map(function (s) { return { name: s.name, seeded: s.seeded, about: s.about }; }); },
      space: function () { return { name: spaceName, seed: spaceSeed, props: space.props.length }; },
      surfaces: function () { return discovered.map(surfaceJson); },
      capture: capture,
      lastCapture: function () { return last; },
      buildRequest: buildRequest,
      raycast: function (origin, direction, maxDistance) { return castReal(origin, direction, maxDistance === undefined ? 10 : maxDistance); },
      viewPoint: function (view, u, v) { var r = viewRay(view, u, v); return castReal(r.origin, r.direction, 12); },
      onEvent: function (cb) { listeners.push(cb); return function () { var i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); }; },
      setFinder: function (fn) { finder = typeof fn === 'function' ? fn : defaultFinder; },
      setTheme: setTheme,
      setCamera: function (which) { mode = which === 'orbit' ? 'orbit' : 'first'; canvas.style.cursor = mode === 'first' ? 'grab' : 'move'; return mode; },
      camera: function () { return mode; },
      userPose: function () { return { position: head.position.slice(), yaw: head.yaw, pitch: head.pitch }; },
      setUserPose: function (pose) {
        if (pose.position) head.position = [pose.position[0], pose.position[1], pose.position[2]];
        if (pose.yaw !== undefined) head.yaw = pose.yaw;
        if (pose.pitch !== undefined) head.pitch = clamp(pose.pitch, -75, 75);
      },
      walk: walk,
      look: function (yawDegrees, pitchDegrees) { head.yaw += yawDegrees || 0; head.pitch = clamp(head.pitch + (pitchDegrees || 0), -75, 75); },
      // Advances the lab by hand; used by tests and by anything that needs deterministic time.
      advance: function (seconds, stepSeconds) { var s = stepSeconds || 1 / 30; for (var t = 0; t < seconds; t += s) step(s); },
      // Where a node (found by a predicate over { id, kind, props, experience }) sits on screen, in CSS pixels.
      locate: function (predicate) {
        var ids = Object.keys(nodes);
        for (var i = 0; i < ids.length; i++) {
          var n = nodes[ids[i]];
          if (!predicate({ id: n.id, kind: n.kind, props: n.props, experience: n.exp.id }) || !isShown(n)) continue;
          contentGroup.updateMatrixWorld(true);
          var v = new THREE.Vector3();
          (n.main || n.fill || n.obj).getWorldPosition(v);
          v.project(activeCamera());
          if (v.z > 1) continue;
          return { x: (v.x + 1) / 2 * width, y: (1 - v.y) / 2 * height, id: n.id };
        }
        return null;
      },
      nodes: function (experienceId) {
        return Object.keys(nodes).map(function (id) { return nodes[id]; })
          .filter(function (n) { return !experienceId || n.exp.id === experienceId; })
          .map(function (n) { return { id: n.id, kind: n.kind, parent: n.parent, props: n.props, experience: n.exp.id }; });
      },
      resize: function () { width = height = 0; resize(); },
      dispose: dispose
    };
  }

  window.SpatailLab = { create: create, spaceNames: SPACES.map(function (s) { return s.name; }) };
})();
