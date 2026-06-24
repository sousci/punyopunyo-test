# Puyo Puyo-style prototype

Open this project in Unity 6000.3.17f1 and press Play in `Assets/Scenes/SampleScene.unity`.
No scene setup or Unity UI components are required: `PuyoGameController` creates the game board, camera, colored pieces, and TextMesh score display at runtime.

Controls:

- Left / Right (or A / D): move
- Up / X and Z: rotate
- Down / S: soft drop
- Space: hard drop
- R: restart after a game over (or at any time)

Four or more connected pieces of the same color disappear. Remaining pieces fall, allowing chain reactions. Score is `cleared pieces × 10 × chain count`.
