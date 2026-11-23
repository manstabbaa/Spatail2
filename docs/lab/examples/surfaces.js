// "Show me what you have found so far, and keep it up to date"
// Sensing only. Nothing was scanned beforehand: surfaces appear in space.surfaces() as the device finds them,
// and this draws each one the moment it shows up.
const colours = { floor: 'accent', wall: 'accentDeep', table: 'success' };
const drawn = {};
let counter = null, count = 0;

function show(s) {
  const colour = colours[s.kind] || 'accent';
  const w = s.size[0], wall = s.kind === 'wall';
  const y = s.kind === 'table' ? s.size[1] / 2 + 0.012 : 0.012;
  const corners = wall
    ? [[-w / 2, -s.size[1] / 2, 0.02], [w / 2, -s.size[1] / 2, 0.02], [w / 2, s.size[1] / 2, 0.02], [-w / 2, s.size[1] / 2, 0.02]]
    : [[-w / 2, y, -s.size[2] / 2], [w / 2, y, -s.size[2] / 2], [w / 2, y, s.size[2] / 2], [-w / 2, y, s.size[2] / 2]];
  // Pull the outline in a hand's width so neighbouring surfaces do not share an edge.
  const points = corners.map(c => vec.add(s.position, quat.rotate(s.rotation, c.map(v => v - Math.sign(v) * (Math.abs(v) > 0.2 ? 0.1 : 0)))));
  points.push(points[0]);
  scene.line({ points, width: 0.014, color: colour });

  const area = wall ? w * s.size[1] : w * s.size[2];
  const name = s.kind.charAt(0).toUpperCase() + s.kind.slice(1);
  // Label it where the user was looking when it was found, snapped onto the surface.
  const look = space.raycast(user.head.position, user.head.forward, 10);
  let at = wall ? vec.add(s.position, quat.rotate(s.rotation, [0, 0.2, 0.03])) : vec.add(s.position, quat.rotate(s.rotation, [0, y, 0]));
  if (look && !wall && s.kind === 'floor' && Math.abs(look.position[1] - s.position[1]) < 0.05) at = look.position;
  kit.appear(kit.label(`${name}  ·  ${area.toFixed(1)} m²`, at, { color: colour, style: 'solid', stem: wall ? 0 : 0.5 }));
}

function scan() {
  for (const s of space.surfaces()) {
    if (drawn[s.id]) continue;
    drawn[s.id] = true;
    count++;
    show(s);
  }
  // A running count that stays with the user.
  const text = count === 0 ? 'Looking for surfaces...' : `${count} surface${count === 1 ? '' : 's'} found`;
  if (!counter || counter.data.text !== text) {
    if (counter) counter.remove();
    counter = kit.pill(text, { style: 'outline', size: theme.text.small, billboard: 'full' });
    counter.data.text = text;
  }
}

scene.onFrame(() => {
  if (!counter) return;
  const head = user.head;
  const flat = vec.normalize(vec.flat(head.forward));
  const target = vec.add(vec.add(head.position, vec.scale(flat, 0.9)), [0, -0.32, 0]);
  counter.position = vec.lerp(counter.position, target, 0.12);
});
scan();
scene.every(0.4, scan);
