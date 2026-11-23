// "How high is that?"
// Sensing only. A ruler from the floor straight up to whatever was at the centre of the view.
const hit = (space.view && space.view.point(0.5, 0.5)) || space.raycast(user.head.position, user.head.forward, 12);
if (!hit) {
  const at = vec.add(user.head.position, vec.scale(user.head.forward, 1.2));
  kit.appear(kit.pill('Look at the thing to measure and ask again', { style: 'outline', position: at, billboard: 'full' }));
  scene.after(3, finish);
  return;
}

// Stand the ruler just proud of the surface so it is not buried in it.
const top = vec.add(hit.position, vec.scale(hit.normal, 0.03));
const base = [top[0], space.floorY, top[2]];
const height = top[1] - base[1];
const ruler = scene.group({ position: base, rotation: space.faceUser(base) });

scene.line({ parent: ruler, points: [[0, 0, 0], [0, height, 0]], width: 0.008, color: 'accent' });
// A tick every ten centimetres, longer and numbered every half metre.
for (let cm = 0; cm <= Math.floor(height * 10) * 10; cm += 10) {
  const major = cm % 50 === 0, y = cm / 100, w = major ? 0.07 : 0.035;
  scene.line({ parent: ruler, points: [[0, y, 0], [w, y, 0]], width: major ? 0.005 : 0.003, color: 'accent' });
  if (major && cm > 0) scene.text({ parent: ruler, text: (cm / 100).toFixed(1), size: theme.text.small, color: 'accentDeep', position: [w + 0.015, y, 0] });
}
scene.line({ parent: ruler, points: [[-0.06, height, 0], [0.1, height, 0]], width: 0.006, color: 'accent' });
scene.sphere({ radius: 0.016, position: hit.position, color: 'accent', shade: false });

const tag = kit.pill(`${height.toFixed(2)} m`, { style: 'solid', position: vec.add(top, [0, 0.07, 0]), billboard: 'full' });
kit.appear(tag);

// Which is taller, this or the user? A small, live comparison.
const note = scene.text({ text: '', size: theme.text.small, color: 'muted', align: 'center', position: vec.add(top, [0, 0.13, 0]), billboard: 'full' });
scene.every(0.5, () => {
  const d = user.head.position[1] - space.floorY - height;
  note.text = Math.abs(d) < 0.05 ? 'level with your eyes' : `${Math.abs(d).toFixed(2)} m ${d > 0 ? 'below' : 'above'} your eye line`;
});
