// "Mark a two metre exclusion zone around that panel"
// The panel is in the middle of the frame. Drop to the floor beneath it and draw the zone there.
const hit = space.view ? space.view.point(0.52, 0.44) : null;
const centre = space.floorBelow(hit ? hit.position : vec.add(user.head.position, vec.scale(vec.flat(user.head.forward), 2)));
const radius = 2;

const zone = scene.group({ position: vec.add(centre, [0, 0.01, 0]) });
scene.disc({ parent: zone, radius, color: 'warning', opacity: 0.12, shade: false });
const rim = scene.torus({ parent: zone, radius, tube: 0.012, color: 'warning', shade: false });
scene.onFrame((dt, t) => rim.opacity = 0.6 + 0.4 * Math.sin(t * 3));

// Warn when the user steps inside.
const notice = kit.pill('Keep clear: 2 m', { color: 'warning', position: vec.add(centre, [0, 1.3, 0]), billboard: 'full' });
let inside = false;
scene.every(0.2, () => {
  const now = vec.distance(vec.flat(user.head.position), vec.flat(centre)) < radius;
  if (now === inside) return;
  inside = now;
  notice.label.text = inside ? 'You are inside the zone' : 'Keep clear: 2 m';
  notice.scale = inside ? 1.25 : 1;
});
voice.on(['clear the zone', 'remove the zone', 'done'], () => finish());
