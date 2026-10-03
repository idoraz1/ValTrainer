using Godot;

namespace ValTrainer.Maps;

public static partial class MapSpots
{
    /// <summary>
    /// Icebox, B Main (Green) → B Site, at true scale. Walls traced from the official minimap (valorant-api.com,
    /// 1 px = 0.136 m; within ~0.4 m of its wall lines) and placed with its own transform; heights from the callout
    /// elevations (B ground 0, B Hall +4.0, Kitchen +3.8, Tube +3.6, Hut +3.5) and reference screenshots.
    /// Authored directly in the final in-game frame (no Mirrored()): origin = the mouth of the Green lane (the choke),
    /// -Z = north (Yellow, the crane platform, Snowman), +X = east (the site, Back B, Kitchen).
    /// You come out of the Garage door into Green, walk up the lane under the container hanging from the crane and
    /// step out at Yellow. The site is to the east: Top Site (a container on a concrete base, with a cubby facing Green
    /// below it) in the middle, the Bridge from B Hall (upper floor of the Research building) to its east door,
    /// Snowman in the north-east corner, the closed Back B hut along the east cliff, Orange to the south.
    /// Upper floor: B Hall → Kitchen (window over the Snow Pile, B Tube stub) → Hut walkway with stairs down to the
    /// Hut yard, which leads back to Back B under the walkway. Cut-offs where the real map goes on (Garage, Mid,
    /// Tube toward Mid, the defender-spawn side of the Hut) are sealed.
    /// </summary>
    public static readonly MapSpot Icebox = new MapSpot
    {
        Key = "icebox", Map = "Icebox", Name = "B Main → B Site",
        Drill = "Come out of Garage into Green, push up to Yellow and clear Yellow, Back Yellow, B Site, the cubby under Top Site, Top Site, Bridge, Hall, Default, Snowman, Back B and Orange",
        StartFeet = V(-9.22f, 0, 18.45f), StartYaw = 55, Choke = V(0, 0, 0),
        Palette = new MapPalette(C(232, 238, 246), C(214, 212, 226), C(222, 184, 70), C(214, 124, 62), C(128, 134, 148), C(170, 196, 230)),
        Boxes = new[]
        {
            B(-12.61f, -0.1f, -19.53f, 59.27f, 0, 42.86f, Surf.Floor), // ground (snow)
            B(-12.61f, 0, -19.53f, -4.75f, 8, -1.9f), // west block (behind Cubby / Green west wall)
            B(-12.61f, 0, -1.9f, -8.14f, 8, 5.02f),
            B(-12.61f, 0, 5.02f, -5.97f, 8, 15.6f),
            B(-12.61f, 0, 15.6f, -11.39f, 8, 20.21f),
            B(-12.61f, 0, 20.21f, 12.75f, 8, 21.43f), // Green lane cut-off (container stack beyond)
            B(-12.61f, 0, 21.43f, 12.75f, 8, 42.86f), // south-west block (Garage / Mid)
            B(-4.75f, 0, -19.53f, 24.55f, 8.5f, -18.24f), // north outer wall (Yellow)
            B(24.55f, 0, -19.53f, 37.3f, 8.5f, -18.24f), // outer wall behind the crane platform
            B(24.55f, 0, -18.24f, 37.3f, 3, -14.65f, Surf.Elevated), // crane platform
            B(24.55f, 0, -14.65f, 37.25f, 3, -14.51f, Surf.Elevated),
            B(24.8f, 0, -14.51f, 36.94f, 3, -13.89f, Surf.Elevated),
            B(25.29f, 0, -13.89f, 36.44f, 3, -13.26f, Surf.Elevated),
            B(25.78f, 0, -13.26f, 35.93f, 3, -12.63f, Surf.Elevated),
            B(26.27f, 0, -12.63f, 35.43f, 3, -12, Surf.Elevated),
            B(26.76f, 0, -12, 34.92f, 3, -11.38f, Surf.Elevated),
            B(27.25f, 0, -11.38f, 34.42f, 3, -10.75f, Surf.Elevated),
            B(27.75f, 0, -10.75f, 33.92f, 3, -10.12f, Surf.Elevated),
            B(28.24f, 0, -10.12f, 33.41f, 3, -9.49f, Surf.Elevated),
            B(28.62f, 3, -17.36f, 33.23f, 8.5f, -12.75f), // crane base tower
            B(37.3f, 0, -19.53f, 59.27f, 9, -16.89f), // east block (Snowman cliff / Kingdom building / Back B cliff)
            B(48.32f, 0, -16.89f, 59.27f, 9, -16.33f),
            B(49.2f, 0, -16.33f, 59.27f, 9, -15.77f),
            B(50.08f, 0, -15.77f, 59.27f, 9, -15.21f),
            B(50.96f, 0, -15.21f, 59.27f, 9, -14.65f),
            B(51.41f, 0, -14.65f, 59.27f, 9, -1.76f),
            B(56.57f, 0, -1.76f, 59.27f, 9, -1.14f),
            B(56.87f, 0, -1.14f, 59.27f, 9, -0.52f),
            B(57.17f, 0, -0.52f, 59.27f, 9, 0.11f),
            B(57.47f, 0, 0.11f, 59.27f, 9, 0.73f),
            B(57.77f, 0, 0.73f, 59.27f, 9, 1.36f),
            B(57.47f, 0, 1.36f, 59.27f, 9, 1.99f),
            B(56.59f, 0, 1.99f, 59.27f, 9, 2.62f),
            B(55.71f, 0, 2.62f, 59.27f, 9, 3.26f),
            B(54.83f, 0, 3.26f, 59.27f, 9, 3.89f),
            B(53.95f, 0, 3.89f, 59.27f, 9, 4.52f),
            B(53.07f, 0, 4.52f, 59.27f, 9, 5.15f),
            B(52.63f, 0, 5.15f, 59.27f, 9, 6.24f),
            B(52.83f, 0, 6.24f, 59.27f, 9, 6.78f),
            B(53.24f, 0, 6.78f, 59.27f, 9, 7.32f),
            B(53.29f, 0, 7.32f, 59.27f, 9, 7.87f),
            B(53, 0, 7.87f, 59.27f, 9, 8.41f),
            B(52.7f, 0, 8.41f, 59.27f, 9, 8.95f),
            B(52.56f, 0, 8.95f, 59.27f, 9, 16.41f),
            B(52.39f, 0, 16.41f, 59.27f, 9, 16.99f),
            B(52.05f, 0, 16.99f, 59.27f, 9, 17.56f),
            B(51.71f, 0, 17.56f, 59.27f, 9, 18.14f),
            B(51.37f, 0, 18.14f, 59.27f, 9, 18.72f),
            B(51.2f, 0, 18.72f, 59.27f, 9, 19.8f),
            B(51.34f, 0, 19.8f, 59.27f, 9, 20.35f),
            B(51.61f, 0, 20.35f, 59.27f, 9, 20.89f),
            B(51.88f, 0, 20.89f, 59.27f, 9, 21.43f),
            B(52.15f, 0, 21.43f, 59.27f, 9, 21.97f),
            B(52.29f, 0, 21.97f, 59.27f, 9, 29.98f),
            B(53.17f, 0, 29.98f, 59.27f, 9, 36.89f),
            B(42.32f, 0, 36.89f, 59.27f, 9, 42.86f), // Hut stairs cut-off (toward defender spawn)
            B(12.75f, 0, 34.59f, 22.65f, 8, 42.86f), // Tube west part + Mid (cut off)
            B(22.65f, 0, 38.79f, 32.01f, 9, 39.33f), // south of Tube / Kitchen
            B(22.65f, 0, 39.33f, 32.65f, 9, 39.88f),
            B(22.65f, 0, 39.88f, 33.94f, 9, 40.42f),
            B(22.65f, 0, 40.42f, 35.23f, 9, 40.96f),
            B(22.65f, 0, 40.96f, 36.52f, 9, 41.5f),
            B(22.65f, 0, 41.5f, 40.62f, 9, 42.86f),
            B(38.86f, 0, 26.04f, 42.32f, 9, 26.58f), // Kitchen east wall block
            B(38.99f, 0, 26.58f, 42.32f, 9, 27.13f),
            B(39.24f, 0, 27.13f, 42.32f, 9, 27.67f),
            B(39.49f, 0, 27.67f, 42.32f, 9, 28.21f),
            B(39.74f, 0, 28.21f, 42.32f, 9, 28.75f),
            B(39.99f, 0, 28.75f, 42.32f, 9, 29.3f),
            B(40.24f, 0, 29.3f, 42.32f, 9, 29.84f),
            B(40.5f, 0, 29.84f, 42.32f, 9, 30.38f),
            B(40.62f, 0, 30.38f, 42.32f, 9, 42.86f),
            B(6.1f, 0, 0.27f, 16.41f, 2.6f, 10.04f, Surf.Cover), // container stack between Green and Orange (3 high)
            B(6.1f, 0, 10.04f, 16.41f, 2.6f, 19.94f, Surf.Cover),
            B(6.1f, 2.6f, 0.27f, 16.41f, 5.2f, 10.04f, Surf.Cover),
            B(6.1f, 2.6f, 10.04f, 16.41f, 5.2f, 19.94f, Surf.Cover),
            B(6.1f, 5.2f, 0.27f, 16.41f, 7.8f, 10.04f, Surf.Cover),
            B(6.1f, 5.2f, 10.04f, 16.41f, 7.8f, 19.94f, Surf.Cover),
            B(6.1f, 0, 19.94f, 12.75f, 7.8f, 20.21f), // wall south of the container stack
            B(22.11f, 0, 13.56f, 26.86f, 2.6f, 27.13f, Surf.Cover), // container stack by Orange / Snow Pile (3 high)
            B(26.86f, 0, 13.56f, 31.6f, 2.6f, 27.13f, Surf.Cover),
            B(22.11f, 2.6f, 13.56f, 26.86f, 5.2f, 27.13f, Surf.Cover),
            B(26.86f, 2.6f, 13.56f, 31.6f, 5.2f, 27.13f, Surf.Cover),
            B(22.11f, 5.2f, 13.56f, 26.86f, 7.8f, 27.13f, Surf.Cover),
            B(26.86f, 5.2f, 13.56f, 31.6f, 7.8f, 27.13f, Surf.Cover),
            B(31.6f, 0, -2.31f, 42.18f, 10.5f, 1.15f), // Research building, north end
            B(34.65f, 0, 1.15f, 42.18f, 10.5f, 22.38f), // Research building, offices east of B Hall
            B(31.6f, 0, 1.15f, 32.15f, 10.5f, 2.31f), // Research west wall (bridge door)
            B(31.6f, 0, 2.31f, 32.15f, 3.8f, 4.75f, Surf.Elevated), // Research west wall (bridge door) (sill)
            B(31.6f, 6.2f, 2.31f, 32.15f, 10.5f, 4.75f), // Research west wall (bridge door) (above opening)
            B(31.6f, 0, 4.75f, 32.15f, 10.5f, 13.56f), // Research west wall (bridge door)
            B(31.6f, 0, 13.56f, 32.15f, 9, 28.62f), // Kitchen west wall (window, Tube)
            B(31.6f, 0, 28.62f, 32.15f, 3.8f, 30.79f, Surf.Elevated), // Kitchen west wall (window, Tube) (sill)
            B(31.6f, 6, 28.62f, 32.15f, 9, 30.79f), // Kitchen west wall (window, Tube) (above opening)
            B(31.6f, 0, 30.79f, 32.15f, 9, 34.86f), // Kitchen west wall (window, Tube)
            B(31.6f, 0, 34.86f, 32.15f, 3.6f, 38.52f, Surf.Elevated), // Kitchen west wall (window, Tube) (sill)
            B(31.6f, 6.6f, 34.86f, 32.15f, 9, 38.52f), // Kitchen west wall (window, Tube) (above opening)
            B(31.6f, 0, 38.52f, 32.15f, 9, 39.47f), // Kitchen west wall (window, Tube)
            B(32.15f, 0, 1.15f, 34.65f, 3.8f, 22.38f, Surf.Elevated), // B Hall floor
            B(32.15f, 6.8f, 1.15f, 34.65f, 10.5f, 22.38f), // B Hall ceiling / upper floor
            B(33.77f, 3.8f, 15.19f, 34.45f, 4.6f, 16.28f, Surf.Cover), // B Hall crate
            B(33.81f, 4.6f, 15.23f, 34.41f, 5.4f, 16.24f, Surf.Cover), // B Hall crate (stacked)
            B(32.15f, 0, 22.38f, 40.62f, 3.8f, 41.5f, Surf.Elevated), // B Kitchen floor
            B(40.62f, 0, 22.38f, 42.32f, 3.8f, 26.04f, Surf.Elevated), // B Kitchen floor (door to Hut)
            B(32.15f, 7, 22.38f, 40.62f, 8.95f, 41.5f), // B Kitchen ceiling
            B(40.62f, 7, 22.38f, 42.05f, 8.95f, 26.04f),
            B(42.05f, 3.8f, 22.38f, 42.32f, 9, 23.33f), // Kitchen door to Hut
            B(42.05f, 6.2f, 23.33f, 42.32f, 9, 25.77f), // Kitchen door to Hut (above opening)
            B(42.05f, 3.8f, 25.77f, 42.32f, 9, 26.04f), // Kitchen door to Hut
            B(38.25f, 3.8f, 27.4f, 39.74f, 4.8f, 28.48f, Surf.Elevated), // Kitchen counter
            B(38.66f, 3.8f, 28.48f, 40.22f, 4.8f, 29.57f, Surf.Elevated),
            B(39.13f, 3.8f, 29.57f, 40.62f, 4.8f, 30.65f, Surf.Elevated),
            B(39.61f, 3.8f, 30.65f, 40.62f, 4.8f, 31.74f, Surf.Elevated),
            B(40.15f, 3.8f, 31.74f, 40.62f, 4.8f, 32.82f, Surf.Elevated),
            B(32.15f, 3.8f, 31.74f, 32.82f, 5.8f, 32.96f, Surf.Cover), // Kitchen fridge
            B(22.11f, 0, 34.59f, 32.15f, 8.5f, 34.86f), // B Tube north wall
            B(22.11f, 0, 34.86f, 22.65f, 8.5f, 38.79f), // B Tube cut-off
            B(22.65f, 0, 34.86f, 31.6f, 3.6f, 38.52f, Surf.Elevated), // B Tube floor
            B(22.65f, 6.6f, 34.86f, 31.6f, 8.5f, 38.52f), // B Tube ceiling
            B(22.65f, 0, 38.52f, 32.01f, 8.5f, 38.79f), // B Tube south wall
            B(24.82f, 3.6f, 37.43f, 26.45f, 4.6f, 38.52f, Surf.Cover), // Tube boxes
            B(24.89f, 4.6f, 37.5f, 26.38f, 5.6f, 38.45f, Surf.Cover), // Tube boxes (stacked)
            B(42.32f, 3.2f, 22.24f, 52.29f, 3.5f, 29.98f, Surf.Elevated), // Hut walkway
            B(42.32f, 0, 22.24f, 45.98f, 3.2f, 29.98f), // under the Hut walkway
            B(48.69f, 0, 22.24f, 52.29f, 3.2f, 29.98f),
            B(42.32f, 0, 29.98f, 45.17f, 3.23f, 30.43f, Surf.Elevated), // Hut stairs
            B(42.32f, 0, 30.43f, 45.17f, 2.96f, 30.88f, Surf.Elevated),
            B(42.32f, 0, 30.88f, 45.17f, 2.69f, 31.33f, Surf.Elevated),
            B(42.32f, 0, 31.33f, 45.17f, 2.42f, 31.78f, Surf.Elevated),
            B(42.32f, 0, 31.78f, 45.17f, 2.15f, 32.24f, Surf.Elevated),
            B(42.32f, 0, 32.24f, 45.17f, 1.88f, 32.69f, Surf.Elevated),
            B(42.32f, 0, 32.69f, 45.17f, 1.62f, 33.14f, Surf.Elevated),
            B(42.32f, 0, 33.14f, 45.17f, 1.35f, 33.59f, Surf.Elevated),
            B(42.32f, 0, 33.59f, 45.17f, 1.08f, 34.04f, Surf.Elevated),
            B(42.32f, 0, 34.04f, 45.17f, 0.81f, 34.5f, Surf.Elevated),
            B(42.32f, 0, 34.5f, 45.17f, 0.54f, 34.95f, Surf.Elevated),
            B(42.32f, 0, 34.95f, 45.17f, 0.27f, 35.4f, Surf.Elevated),
            B(24.14f, 0, -4.41f, 26.11f, 3.7f, 7.66f), // Top Site base
            B(21.97f, 0, -4.41f, 24.14f, 3.7f, -2.31f), // Top Site base (north pier)
            B(21.97f, 0, 5.63f, 24.14f, 3.7f, 7.66f), // Top Site base (south pier)
            B(21.97f, 2.5f, -2.31f, 24.14f, 3.7f, 5.63f), // Top Site base over the cubby
            B(21.97f, 3.7f, -4.41f, 26.11f, 3.8f, 7.66f, Surf.Elevated), // Top Site container floor
            B(21.97f, 3.8f, -4.41f, 22.12f, 6.5f, -2.44f, Surf.Cover), // Top Site container, west side (doorway)
            B(21.97f, 6, -2.44f, 22.12f, 6.5f, 1.36f, Surf.Cover), // Top Site container, west side (doorway) (above opening)
            B(21.97f, 3.8f, 1.36f, 22.12f, 6.5f, 7.66f, Surf.Cover), // Top Site container, west side (doorway)
            B(25.96f, 3.8f, -4.41f, 26.11f, 6.5f, 2.31f, Surf.Cover), // Top Site container, east side (bridge door)
            B(25.96f, 6.1f, 2.31f, 26.11f, 6.5f, 4.75f, Surf.Cover), // Top Site container, east side (bridge door) (above opening)
            B(25.96f, 3.8f, 4.75f, 26.11f, 6.5f, 7.66f, Surf.Cover), // Top Site container, east side (bridge door)
            B(22.12f, 3.8f, 7.51f, 25.96f, 6.5f, 7.66f, Surf.Cover), // Top Site container, south end
            B(21.97f, 6.5f, -4.41f, 26.11f, 6.8f, 7.66f, Surf.Cover), // Top Site container roof
            B(24.82f, 3.8f, 5.7f, 25.91f, 4.9f, 6.78f, Surf.Cover), // crate in Top Site
            B(26.11f, 3.55f, 2.17f, 31.6f, 3.8f, 4.95f, Surf.Elevated), // bridge (Hall -> Top Site)
            B(0.95f, 0, -11.94f, 10.92f, 0.45f, -7.93f, Surf.Elevated), // Yellow container plinth
            B(1.15f, 0.45f, -11.73f, 10.72f, 2.9f, -8.14f, Surf.Trim), // Yellow container
            B(8.95f, 0, -7.87f, 10.92f, 1.15f, -5.7f, Surf.Cover), // crates beside Yellow
            B(9.09f, 1.15f, -7.73f, 10.78f, 2.3f, -5.83f, Surf.Cover), // crates beside Yellow (stacked)
            B(-4.75f, 0, -18.17f, -3.12f, 1.2f, -15.94f, Surf.Cover), // crates in the Yellow corner
            B(-3.12f, 0, -18.17f, -1.76f, 1, -16.95f, Surf.Cover), // crate in the Yellow corner
            B(-0.95f, 4.6f, 0.34f, 2.44f, 7.2f, 10.17f, Surf.Cover), // container hanging from the crane over Green
            B(-0.95f, 0, 7.05f, 1.22f, 2.2f, 9.22f, Surf.Cover), // KNG crate in Green
            B(-2.98f, 0, 7.19f, -0.95f, 1.1f, 9.22f, Surf.Cover), // crate in Green
            B(-2.03f, 0, 9.22f, -0.95f, 1, 10.17f, Surf.Cover), // barrels in Green
            B(3.93f, 0, 14.24f, 5.97f, 1.15f, 16.28f, Surf.Cover), // crates in Green
            B(4.07f, 1.15f, 14.38f, 5.83f, 2.3f, 16.14f, Surf.Cover), // crates in Green (stacked)
            B(16.68f, 0, 16.95f, 18.51f, 1.15f, 20.14f, Surf.Cover), // Orange crates
            B(16.82f, 1.15f, 17.09f, 18.38f, 2.3f, 20.01f, Surf.Cover), // Orange crates (stacked)
            B(46.93f, 0, 5.7f, 52.76f, 3.2f, 14.38f), // Back B hut (closed)
            B(27.4f, 0, 28.62f, 27.78f, 0.34f, 30.79f, Surf.Floor), // snow pile up to the Kitchen window
            B(27.78f, 0, 28.62f, 28.16f, 0.69f, 30.79f, Surf.Floor),
            B(28.16f, 0, 28.62f, 28.54f, 1.04f, 30.79f, Surf.Floor),
            B(28.54f, 0, 28.62f, 28.93f, 1.38f, 30.79f, Surf.Floor),
            B(28.93f, 0, 28.62f, 29.31f, 1.73f, 30.79f, Surf.Floor),
            B(29.31f, 0, 28.62f, 29.69f, 2.07f, 30.79f, Surf.Floor),
            B(29.69f, 0, 28.62f, 30.07f, 2.42f, 30.79f, Surf.Floor),
            B(30.07f, 0, 28.62f, 30.46f, 2.76f, 30.79f, Surf.Floor),
            B(30.46f, 0, 28.62f, 30.84f, 3.11f, 30.79f, Surf.Floor),
            B(30.84f, 0, 28.62f, 31.22f, 3.46f, 30.79f, Surf.Floor),
            B(31.22f, 0, 28.62f, 31.6f, 3.8f, 30.79f, Surf.Floor),
            B(29.7f, 0, 26.86f, 31.06f, 1.15f, 28.48f, Surf.Cover), // crates by the snow pile
            B(29.77f, 1.15f, 26.92f, 30.99f, 2.3f, 28.42f, Surf.Cover), // crates by the snow pile (stacked)
            B(29.7f, 0, 30.92f, 31.06f, 1.15f, 32.28f, Surf.Cover), // crate by the snow pile
        },
        Enemies = new[]
        {
            E("Yellow", 0.27f, 0, -13.7f),
            E("Back Yellow", -3.12f, 0, -14.92f, Stance.Crouch),
            E("B Site", 19.94f, 0, -4.88f),
            E("Under Top Site", 22.92f, 0, -1.22f, Stance.Crouch),
            E("Top Site", 24.01f, 3.8f, -0.27f),
            E("Bridge", 29.16f, 3.8f, 3.53f, Stance.Crouch),
            E("Hall", 33.37f, 3.8f, 2.98f),
            E("Default", 19.53f, 0, 6.51f, Stance.Crouch),
            E("Snowman", 43.27f, 0, -8.68f),
            E("Back B", 49.1f, 0, 15.73f),
            E("Orange", 19.4f, 0, 14.65f),
        },
        Flashes = new[]
        {
            F("Pop over the Green exit", V(0.68f, 1.6f, 2.98f), V(2.03f, 2.6f, -2.44f)),
            F("Lob over Yellow", V(-1.09f, 1.7f, 2.17f), V(2.31f, 3.6f, -10.04f)),
            F("Curve right onto site", V(2.98f, 1.6f, 1.63f), V(12.48f, 3, -3.26f)),
            F("High pop over Top Site", V(-0.41f, 1.7f, 1.09f), V(19.94f, 5.5f, -2.58f)),
        },
        PlantSpots = new[] { V(19.53f, 0, 6.51f), V(17.9f, 0, -2.98f), V(29.16f, 0, -1.22f) }, // Default (west of Top Site), open site toward Yellow, back site behind Top Site
        RetakeEntries = new[] { V(33.37f, 3.8f, 20.89f), V(49.1f, 0, 33.09f), V(27.4f, 0, 32.69f) }, // B Hall from Kitchen, Hut yard, Snow Pile
        Smokes = new[] { new SmokeSpot("Snowman", V(42.32f, 0, -8.68f)), new SmokeSpot("Top Site", V(24.28f, 3.8f, 1.09f)), new SmokeSpot("Orange", V(19.26f, 0, 11.66f)) },
        Look = IceboxLook,
    };

    /// <summary>Arctic port: snow (Poly Haven snow_02), pale Kingdom panels, orange corrugated containers
    /// (Poly Haven container_side), yellow container, metal decks, cold low sun and icy mountains.</summary>
    static World.Theme IceboxLook(MapPalette p) => new()
    {
        Key = "icebox", Style = World.DecorStyle.Range, Hdri = World.Hdris.Qwantani, SkyYaw = 140,
        SkyTint = new Color(0.86f, 0.93f, 1.08f), SkySaturation = 0.85f,
        SunColor = new Color(1f, 0.9f, 0.9f), SunEnergy = 1.7f, Ambient = 1.1f, Ground = C(200, 208, 220),
        Saturation = 1.04f, Contrast = 1.04f, FogDensity = 0.0018f,
        Floor = new World.Surface("snow_02", p.Floor, 1f, 3f, 0.5f, Normal: 0.6f, Macro: 0.12f, Grime: 0f),
        Wall = new World.Surface("concrete_wall_008", p.Wall, 0.9f, 2.7f, 0.8f, Macro: 0.08f, Grime: 0.12f),
        Base = new World.Surface("snow_02", C(236, 241, 248), 1f, 3f, 0.7f, Normal: 0.6f, Grime: 0f),
        Cap = new World.Surface("metal_plate_02", C(74, 78, 90), 0.85f, 2f, 0.6f, Metal: 0.5f, Grime: 0f),
        Trim = new World.Surface("container_side", p.Trim, 1f, 2.6f, 1.2f, Metal: 0.3f, Grime: 0.1f),
        Elevated = new World.Surface("metal_plate_02", p.Elevated, 0.85f, 2f, 0.7f, Metal: 0.6f),
        Cover = new World.Surface("container_side", p.Cover, 1f, 2.6f, 1.2f, Metal: 0.3f, Grime: 0.1f),
        CoverFrame = new World.Surface("metal_plate_02", C(86, 90, 100), 0.85f, 1.6f, 0.6f, Metal: 0.6f, Grime: 0f),
        GroundSurf = new World.Surface("snow_02", C(226, 233, 242), 1f, 3f, 0.7f, Normal: 0.6f, Grime: 0f),
        Backdrop = new World.Surface("concrete_wall_008", Colors.White, 0.9f, 2.7f, 0.9f, Macro: 0.1f),
        Roof = new World.Surface("metal_plate_02", C(96, 100, 112), 0.85f, 2f, 0.6f, Metal: 0.5f, Grime: 0f),
        Hues = new[] { C(232, 230, 240), C(214, 212, 228), C(206, 220, 234), C(190, 192, 204), C(236, 236, 236), C(200, 210, 222) },
        BaseH = 0.35f, CapH = 0.25f, MinH = 9f, MaxH = 16f, Mountains = true, MountainColor = new Color(0.86f, 0.9f, 0.96f),
    };
}
