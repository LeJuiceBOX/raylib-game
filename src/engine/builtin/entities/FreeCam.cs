using Raylib_cs;
using System.Numerics;

namespace PhrawgEngine
{
    /// <summary>
    /// A free-flying camera controller. Add it to the workspace and it will
    /// take over <see cref="Game.camera"/> every frame.
    /// <para>Controls: WASD to move, Q/E to fly down/up, hold Shift to sprint, mouse to look.</para>
    /// </summary>
    public class FreeCam : Entity
    {
        /// <summary>Base movement speed in world units per second.</summary>
        public float MoveSpeed = 100f;

        /// <summary>Speed multiplier applied while Shift is held.</summary>
        public float SprintMultiplier = 1.5f;

        /// <summary>Mouse sensitivity in radians per pixel.</summary>
        public float MouseSensitivity = 0.003f;

        private float _yaw;
        private float _pitch;

        public override void Load()
        {
            Raylib.DisableCursor();

            // Derive initial yaw/pitch from wherever the camera is already pointing.
            Vector3 dir = Vector3.Normalize(Game.CurrentCamera.Target - Game.CurrentCamera.Position);
            _yaw   = MathF.Atan2(dir.Z, dir.X);
            _pitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
        }

        public override void Update(float dt)
        {
            // --- Mouse look ---
            Vector2 mouse = Raylib.GetMouseDelta();
            _yaw   += mouse.X * MouseSensitivity;
            _pitch -= mouse.Y * MouseSensitivity;
            _pitch  = Math.Clamp(_pitch, -1.55f, 1.55f); // ~±89°

            Vector3 forward = Vector3.Normalize(new Vector3(
                MathF.Cos(_pitch) * MathF.Cos(_yaw),
                MathF.Sin(_pitch),
                MathF.Cos(_pitch) * MathF.Sin(_yaw)));

            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

            // --- Keyboard movement ---
            float speed = MoveSpeed * dt;
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift)) speed *= SprintMultiplier;

            Vector3 move = Vector3.Zero;
            if (Raylib.IsKeyDown(KeyboardKey.W)) move += forward;
            if (Raylib.IsKeyDown(KeyboardKey.S)) move -= forward;
            if (Raylib.IsKeyDown(KeyboardKey.D)) move += right;
            if (Raylib.IsKeyDown(KeyboardKey.A)) move -= right;
            if (Raylib.IsKeyDown(KeyboardKey.E)) move += Vector3.UnitY;
            if (Raylib.IsKeyDown(KeyboardKey.Q)) move -= Vector3.UnitY;

            if (move != Vector3.Zero)
                Game.CurrentCamera.Position += Vector3.Normalize(move) * speed;

            Game.CurrentCamera.Target = Game.CurrentCamera.Position + forward;
        }
    }
}
