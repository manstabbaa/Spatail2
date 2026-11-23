namespace Spatail.Core
{
    /// <summary>
    /// A synthetic room for the editor simulator, tests and documentation: a floor, a back wall and two side walls,
    /// with the user standing a couple of metres back from the back wall. No furniture, so site fixtures appear as
    /// stand-in holograms.
    /// </summary>
    public static class SimRoom
    {
        public const float Width = 7f;
        public const float Depth = 6f;
        public const float Height = 2.8f;
        public const float BackWallZ = 0.95f;

        public static RoomModel Create(bool withRealTable = false)
        {
            var room = new RoomModel
            {
                floorY = 0f,
                userPosition = new Vec3(0.1f, 1.6f, -2.3f),
                userForward = Vec3.Forward,
            };
            room.entities.Add(new RoomEntity
            {
                id = "floor", kind = EntityKind.Floor, label = "Floor",
                position = new Vec3(0, 0, BackWallZ - Depth * 0.5f), size = new Vec3(Width, 0, Depth),
            });
            room.entities.Add(new RoomEntity
            {
                id = "wall-back", kind = EntityKind.Wall, label = "Back wall",
                position = new Vec3(0, Height * 0.5f, BackWallZ), rotation = Quat.Yaw(180f), size = new Vec3(Width, Height, 0.02f),
            });
            room.entities.Add(new RoomEntity
            {
                id = "wall-left", kind = EntityKind.Wall, label = "Left wall",
                position = new Vec3(-Width * 0.5f, Height * 0.5f, BackWallZ - Depth * 0.5f), rotation = Quat.Yaw(90f), size = new Vec3(Depth, Height, 0.02f),
            });
            room.entities.Add(new RoomEntity
            {
                id = "wall-right", kind = EntityKind.Wall, label = "Right wall",
                position = new Vec3(Width * 0.5f, Height * 0.5f, BackWallZ - Depth * 0.5f), rotation = Quat.Yaw(-90f), size = new Vec3(Depth, Height, 0.02f),
            });
            if (withRealTable)
            {
                room.entities.Add(new RoomEntity
                {
                    id = "table-1", kind = EntityKind.Table, label = "Table",
                    position = new Vec3(-2.2f, 0.375f, 0.2f), size = new Vec3(1.4f, 0.75f, 0.7f),
                });
            }
            return room;
        }
    }
}
