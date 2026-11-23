// "Put a two metre keep-clear zone on the floor in front of that"
// Sensing only. "That" is whatever was at the centre of the view; the zone sits on the floor in front of it
// and reacts when the user steps inside.
const hit = (space.view && space.view.point(0.5, 0.5)) || space.raycast(user.head.position, user.head.forward, 12);
if (!hit) {
  const at = vec.add(user.head.position, vec.scale(user.head.forward, 1.2));
  kit.appear(kit.pill('Look at the thing to keep clear of and ask again', { style: 'outline', position: at, billboard: 'full' }));
  scene.after(3, finish);
  return;
}

const R = 1.0;
// Step out from the surface that was hit, along the direction it faces. Looking at the floor itself gives a
// vertical normal, in which case the zone is centred on that spot.
const out = vec.flat(hit.normal);
const centre = space.floorBelow(vec.length(out) > 0.3 ? vec.add(hit.position, vec.scale(vec.normalize(out), R + 0.05)) : hit.position);
const lift = h => vec.add(centre, [0, h, 0]);

const zone = scene.group({ position: lift(0) });
const fill = scene.disc({ parent: zone, radius: R, position: [0, 0.006, 0], color: 'warning', opacity: 0.14, shade: false });
const ring = scene.torus({ parent: zone, radius: R, tube: 0.014, position: [0, 0.014, 0], color: 'warning', shade: false });
// Short posts at the compass points make the edge readable from a distance.
for (let i = 0; i < 8; i++) {
  const a = i * Math.PI / 4;
  scene.cylinder({ parent: zone, radius: 0.012, height: 0.22, position: [Math.cos(a) * R, 0.11, Math.sin(a) * R], color: 'warning' });
}
kit.appear(zone);

const edge = vec.add(centre, vec.scale(vec.normalize(vec.flat(vec.sub(user.head.position, centre))), R));
const calm = kit.label('Keep clear  ·  2 m', edge, { color: 'warning', style: 'outline', stem: 1.05 });
const alarm = kit.label('Step back', edge, { color: '#D92D20', style: 'solid', stem: 1.05 });
alarm.visible = false;

let inside = false;
scene.onFrame((dt, time) => {
  const now = vec.length(vec.flat(vec.sub(user.head.position, centre))) < R;
  if (now !== inside) {
    inside = now;
    ring.color = inside ? '#D92D20' : 'warning';
    fill.color = inside ? '#D92D20' : 'warning';
    calm.visible = !inside;
    alarm.visible = inside;
  }
  fill.opacity = inside ? 0.22 + 0.12 * Math.sin(time * 8) : 0.14;
});
