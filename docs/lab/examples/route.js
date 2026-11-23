// "Guide me to that"
// Sensing only. The goal is the floor in front of whatever was at the centre of the view. The path is checked
// with rays at shin and waist height and bends around anything in the way; it is re-planned as the user walks.
const hit = (space.view && space.view.point(0.5, 0.5)) || space.raycast(user.head.position, user.head.forward, 12);
if (!hit) {
  const at = vec.add(user.head.position, vec.scale(user.head.forward, 1.2));
  kit.appear(kit.pill('Look at where you want to go and ask again', { style: 'outline', position: at, billboard: 'full' }));
  scene.after(3, finish);
  return;
}

const out = vec.flat(hit.normal);
const goal = space.floorBelow(vec.length(out) > 0.3 ? vec.add(hit.position, vec.scale(vec.normalize(out), 0.45)) : hit.position);
const feet = () => [user.head.position[0], space.floorY, user.head.position[2]];

function clear(from, to) {
  const d = vec.sub(to, from), length = vec.length(d);
  if (length < 0.05) return true;
  for (const h of [0.25, 0.9]) {
    const blocked = space.raycast([from[0], from[1] + h, from[2]], d, length);
    if (blocked && blocked.distance < length - 0.2) return false;
  }
  return true;
}

function plan(from) {
  if (clear(from, goal)) return [from, goal];
  const dir = vec.normalize(vec.sub(goal, from)), side = [dir[2], 0, -dir[0]], length = vec.distance(from, goal);
  for (const along of [0.5, 0.35, 0.65]) {
    for (const off of [0.8, -0.8, 1.5, -1.5, 2.3, -2.3]) {
      const via = vec.add(vec.add(from, vec.scale(dir, length * along)), vec.scale(side, off));
      if (clear(from, via) && clear(via, goal)) return [from, via, goal];
    }
  }
  return [from, goal];
}

// The destination: a ring that breathes, and how far there is left to walk.
const ring = scene.torus({ radius: 0.28, tube: 0.014, position: vec.add(goal, [0, 0.014, 0]), color: 'accent', shade: false });
scene.disc({ radius: 0.28, position: vec.add(goal, [0, 0.006, 0]), color: 'accent', opacity: 0.16, shade: false });
scene.onFrame((dt, time) => ring.scale = 1 + 0.08 * Math.sin(time * 3));

let path = null, tag = null, arrived = false;
function update() {
  if (arrived) return;
  const from = feet(), points = plan(from);
  let left = 0;
  for (let i = 0; i < points.length - 1; i++) left += vec.distance(points[i], points[i + 1]);
  if (path) path.remove();
  if (tag) tag.remove();
  if (left < 0.6) {
    arrived = true;
    tag = kit.label('You have arrived', goal, { style: 'solid', stem: 1.1 });
    scene.after(2.5, finish);
    return;
  }
  // Start the trail a step ahead of the user's feet so it does not sit under them.
  const ahead = vec.add(from, vec.scale(vec.normalize(vec.sub(points[1], from)), Math.min(0.5, left * 0.2)));
  path = scene.tube({ points: [ahead, ...points.slice(1)].map(p => vec.add(p, [0, 0.03, 0])), radius: 0.03, color: 'accent', opacity: 0.9, flow: { speed: 0.7, density: 5 } });
  tag = kit.label(`${left.toFixed(1)} m`, goal, { style: 'outline', stem: 1.1 });
}
update();
scene.every(0.5, update);
