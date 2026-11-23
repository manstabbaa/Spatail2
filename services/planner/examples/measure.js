// "How far is it from that cabinet to the door?"
// Written after looking at the frame: the cabinet is left of centre, the door is at the right edge.
const a = space.view && space.view.point(0.31, 0.58);
const b = space.view && space.view.point(0.88, 0.52);
if (!a || !b) {
  // Sensing found nothing solid at one of the spots; say so where the user will see it.
  const at = vec.add(user.head.position, vec.scale(vec.flat(user.head.forward), 0.9));
  kit.card({ position: at, rotation: space.faceUser(at, 20), title: 'I could not reach both', body: 'Look at the cabinet and the door together and ask again.' });
} else {
  const metres = vec.distance(a.position, b.position);
  scene.line({ points: [a.position, b.position], width: 0.006, color: 'accent' });
  for (const end of [a, b]) scene.sphere({ radius: 0.02, position: end.position, color: 'accent', shade: false });
  const mid = vec.add(vec.lerp(a.position, b.position, 0.5), [0, 0.08, 0]);
  kit.appear(kit.pill(`${metres.toFixed(2)} m`, { position: mid, billboard: 'full' }));
}
