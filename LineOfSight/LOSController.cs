﻿using RWCustom;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using System;

namespace LineOfSight
{
    class LOSController : CosmeticSprite
    {
        internal static bool hackToDelayDrawingUntilAfterTheLevelMoves;

        //config variables
        public static bool classic;
        public static float visibility;
        public static float brightness;
        public static float tileSize;

        public static FShader fovShader;

        //Sprites
        private TriangleMesh fovBlocker;
        private FSprite screenBlocker;
        //private TriangleMesh fovColorer;

        //Rendering
        public float lastScreenblockAlpha = 1f;
        public float screenblockAlpha = 1f;
        public bool hideAllSprites = false;
        private Vector2? _overrideEyePos;
        private Vector2 _lastOverrideEyePos;

        public enum MappingState
        {
            FindingEdges,
            DuplicatingPoints,
            Done
        }

        //Mapping tiles
        public MappingState state;
        private Room.Tile[,] tiles;
        private int _x;
        private int _y;
        public List<Vector2> corners = new List<Vector2>();
        public List<int> edges = new List<int>();

        public LOSController(Room room)
        {
            _x = 0;
            _y = 0;
            tiles = room.Tiles;

            if (fovShader == null)
                fovShader = classic ? room.game.rainWorld.Shaders["Basic"] : FShader.CreateShader("LOSShader", Assets.LOSShader);
        }

        public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            base.InitiateSprites(sLeaser, rCam);

            while (state != MappingState.Done)
                UpdateMapper(int.MaxValue);

            sLeaser.sprites = new FSprite[2];

            // Generate tris
            TriangleMesh.Triangle[] tris = new TriangleMesh.Triangle[edges.Count];
            for (int i = 0, len = edges.Count / 2; i < len; i++)
            {
                int o = i * 2;
                tris[o] = new TriangleMesh.Triangle(edges[o], edges[o + 1], edges[o] + corners.Count / 2);
                tris[o + 1] = new TriangleMesh.Triangle(edges[o + 1], edges[o + 1] + corners.Count / 2, edges[o] + corners.Count / 2);
            }

            // Block outside of FoV with level color
            fovBlocker = new TriangleMesh("Futile_White", tris, false, true);
            fovBlocker.shader = fovShader;
            corners.CopyTo(fovBlocker.vertices);
            fovBlocker.Refresh();
            sLeaser.sprites[0] = fovBlocker;

            /*fovColorer = new TriangleMesh("Futile_White", tris, false, true);
            fovColorer.shader = room.game.rainWorld.Shaders["Basic"];
            corners.CopyTo(fovColorer.vertices);
            fovColorer.Refresh();
            sLeaser.sprites[2] = fovColorer;*/

            // Full screen overlay

            screenBlocker = new FSprite("pixel")
            {
                anchorX = 0f,
                anchorY = 0f
            };
            screenBlocker.shader = fovShader;
            sLeaser.sprites[1] = screenBlocker;

            AddToContainer(sLeaser, rCam, null);
        }

        public override void ApplyPalette(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
        {
            Color unseenColor = classic ? Color.Lerp(Color.black, palette.blackColor, brightness) : new Color(1f - visibility, 0, 0, 1f);
            
            fovBlocker.color = unseenColor;
            screenBlocker.color = unseenColor;
            //fovColorer.color = unseenColor;
        }

        public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContainer)
        {
            FContainer FLightsContainer = rCam.ReturnFContainer("ForegroundLights");
            FContainer BloomContainer = rCam.ReturnFContainer("Bloom");

            if(LineOfSightMod.classic)
            {
                BloomContainer.AddChild(fovBlocker);
                BloomContainer.AddChild(screenBlocker);
            } 
            else
            {
                FLightsContainer.AddChild(fovBlocker);
                FLightsContainer.AddChild(screenBlocker);
            }
            //BloomContainer.AddChild(fovColorer);
        }

        public override void Update(bool eu)
        {
            base.Update(eu);

            lastScreenblockAlpha = screenblockAlpha;

            hideAllSprites = false;
            if (room.game.IsArenaSession)
            {
                if (!room.game.GetArenaGameSession.playersSpawned)
                    hideAllSprites = true;
            }

            Player ply = null;
            if (room.game.Players.Count > 0)
                ply = room.game.Players[0].realizedCreature as Player;

            // Map edges to display quads
            if (state != MappingState.Done)
                UpdateMapper(300);

            // Do not try to access shortcuts when the room is not ready for AI
            if (!room.readyForAI)
            {
                screenblockAlpha = 1f;
                return;
            }

            // Find the player's shortcut vessel
            ShortcutHandler.ShortCutVessel plyVessel = null;
            foreach (ShortcutHandler.ShortCutVessel vessel in room.game.shortcuts.transportVessels)
            {
                if (vessel.creature == ply)
                {
                    plyVessel = vessel;
                    break;
                }
            }

            if (ply == null || ply.room != room || (plyVessel != null && plyVessel.entranceNode != -1))
                screenblockAlpha = Mathf.Clamp01(screenblockAlpha + 0.1f);
            else
                screenblockAlpha = Mathf.Clamp01(screenblockAlpha - 0.1f);

            if (ply != null)
            {
                // Allow vision when going through shortcuts
                if (plyVessel != null)
                {
                    bool first = !_overrideEyePos.HasValue;
                    if (!first) _lastOverrideEyePos = _overrideEyePos.Value;
                    _overrideEyePos = Vector2.Lerp(plyVessel.lastPos.ToVector2(), plyVessel.pos.ToVector2(), (room.game.updateShortCut + 1) / 3f) * 20f + new Vector2(10f, 10f);
                    if (first) _lastOverrideEyePos = _overrideEyePos.Value;
                    if (plyVessel.room.realizedRoom != null)
                        screenblockAlpha = plyVessel.room.realizedRoom.GetTile(_overrideEyePos.Value).Solid ? 1f : Mathf.Clamp01(screenblockAlpha - 0.2f);
                }
                else
                    _overrideEyePos = null;
            }

            // Don't display in arena while multiple players are present
            // This doesn't happen in story so that Monkland still works
            if (room.game.IsArenaSession && room.game.Players.Count > 1)
                hideAllSprites = true;
        }

        private Vector2 _lastEyePos;
        private Vector2 _eyePos;
        private Vector2 _lastCamPos;

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            if (!hackToDelayDrawingUntilAfterTheLevelMoves)
            {
                _lastCamPos = camPos;
                return;
            }

            _lastEyePos = _eyePos;

            if (sLeaser == null || rCam == null) return;
            if (room == null || room.game == null || sLeaser.sprites == null) return;

            foreach (FSprite sprite in sLeaser.sprites)
                sprite.isVisible = !hideAllSprites;

            if (room.game.Players.Count > 0)
            {
                BodyChunk headChunk = room.game.Players[0].realizedCreature?.bodyChunks[0];
                // Thanks, screams
                if (headChunk != null)
                    _eyePos = Vector2.Lerp(headChunk.lastPos, headChunk.pos, timeStacker);
            }

            if (_overrideEyePos.HasValue)
                _eyePos = Vector2.Lerp(_lastOverrideEyePos, _overrideEyePos.Value, timeStacker);

            // Update FOV blocker mesh
            if (_eyePos != _lastEyePos)
            {
                Vector2 pos;
                pos.x = 0f;
                pos.y = 0f;
                for (int i = 0, len = corners.Count / 2; i < len; i++)
                {
                    pos.Set(corners[i].x - _eyePos.x, corners[i].y - _eyePos.y);
                    pos.Normalize();
                    fovBlocker.vertices[i].Set(corners[i].x, corners[i].y);
                    fovBlocker.vertices[i + len].Set(pos.x * 2000f + _eyePos.x, pos.y * 2000f + _eyePos.y);
                }

                // Calculate FoV blocker UVs
                for (int i = fovBlocker.UVvertices.Length - 1; i >= 0; i--)
                {
                    Vector2 wPos = fovBlocker.vertices[i] - _lastCamPos;
                    fovBlocker.UVvertices[i].x = InverseLerpUnclamped(rCam.levelGraphic.x, rCam.levelGraphic.x + rCam.levelGraphic.width, wPos.x);
                    fovBlocker.UVvertices[i].y = InverseLerpUnclamped(rCam.levelGraphic.y, rCam.levelGraphic.y + rCam.levelGraphic.height, wPos.y);
                }
                fovBlocker.Refresh();
            }

            fovBlocker.x = -_lastCamPos.x;
            fovBlocker.y = -_lastCamPos.y;

            /*fovColorer.vertices = fovBlocker.vertices;
            fovColorer.UVvertices = fovBlocker.UVvertices;
            fovColorer.Refresh();
            fovColorer.x = -_lastCamPos.x;
            fovColorer.y = -_lastCamPos.y;*/

            if (!classic && fovBlocker.element != rCam.levelGraphic.element)
                fovBlocker.element = rCam.levelGraphic.element;

            // Block the screen when inside a wall
            {
                IntVector2 tPos = room.GetTilePosition(_eyePos);
                if (tPos.x < 0) tPos.x = 0;
                if (tPos.x >= room.TileWidth) tPos.x = room.TileWidth - 1;
                if (tPos.y < 0) tPos.y = 0;
                if (tPos.y >= room.TileHeight) tPos.y = room.TileHeight - 1;
                if (tiles[tPos.x, tPos.y].Solid)
                {
                    lastScreenblockAlpha = 1f;
                    screenblockAlpha = 1f;
                }
            }

            // Move the screenblock
            float alpha = Mathf.Lerp(lastScreenblockAlpha, screenblockAlpha, timeStacker);
            if (alpha == 0f)
            {
                screenBlocker.isVisible = false;
                //fovColorer.alpha = 1f;
            }
            else
            {
                screenBlocker.scaleX = rCam.levelGraphic.scaleX;
                screenBlocker.scaleY = rCam.levelGraphic.scaleY;
                screenBlocker.x = rCam.levelGraphic.x;
                screenBlocker.y = rCam.levelGraphic.y;
                if (LineOfSightMod.classic)
                {
                    // Must be resized to fit the level image
                    screenBlocker.width = rCam.levelGraphic.width;
                    screenBlocker.height = rCam.levelGraphic.height;
                }
                else if (screenBlocker.element != rCam.levelGraphic.element)
                    screenBlocker.element = rCam.levelGraphic.element;
                screenBlocker.alpha = alpha;
                //fovColorer.alpha = 1f - alpha;
            }

            // Keep on top
            if (screenBlocker.container.GetChildAt(screenBlocker.container.GetChildCount() - 1) != screenBlocker)
            {
                fovBlocker.MoveToFront();
                screenBlocker.MoveToFront();
            }
            /*if (fovColorer.container.GetChildAt(fovColorer.container.GetChildCount() - 1) != fovColorer)
                fovColorer.MoveToFront();*/

            base.DrawSprites(sLeaser, rCam, timeStacker, _lastCamPos);
        }

        private float InverseLerpUnclamped(float from, float to, float t)
        {
            return (t - from) / (to - from);
        }

        private static Matrix ROTATE_0 = new Matrix(1f, 0f, 0f, 1f);
        private static Matrix ROTATE_90 = new Matrix(0f, 1f, -1f, 0f);
        private static Matrix ROTATE_180 = new Matrix(-1f, 0f, 0f, -1f);
        private static Matrix ROTATE_270 = new Matrix(0f, -1f, 1f, 0f);
        
        private enum Direction
        {
            Up,
            Right,
            Down,
            Left
        }

        public void UpdateMapper(int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                switch (state)
                {
                    case MappingState.FindingEdges:
                        {
                            Room.Tile tile = tiles[_x, _y];
                            Room.Tile.TerrainType terrain = tile.Terrain;
                            Room.SlopeDirection slope = (terrain == Room.Tile.TerrainType.Slope) ? room.IdentifySlope(_x, _y) : Room.SlopeDirection.Broken;

                            if (slope != Room.SlopeDirection.Broken) AddSlopeEdge(_x, _y, slope);
                            if (terrain == Room.Tile.TerrainType.Solid)
                            {
                                if(tileSize == 10f) //old edge detection
                                {
                                    if (HasEdge(_x, _y, Direction.Left) && !HasEdge(_x - 1, _y, Direction.Right)) AddEdge(_x, _y, Direction.Left);
                                    if (HasEdge(_x, _y, Direction.Down) && !HasEdge(_x, _y - 1, Direction.Up)) AddEdge(_x, _y, Direction.Down);
                                    if (HasEdge(_x, _y, Direction.Right) && !HasEdge(_x + 1, _y, Direction.Left)) AddEdge(_x, _y, Direction.Right);
                                    if (HasEdge(_x, _y, Direction.Up) && !HasEdge(_x, _y + 1, Direction.Down)) AddEdge(_x, _y, Direction.Up);
                                }
                                else if (tileSize < 10f) //new edge detection
                                {
                                    CalculateEdge(new Vector2Int(_x, _y), ROTATE_0);
                                    CalculateEdge(new Vector2Int(_x, _y), ROTATE_90);
                                    CalculateEdge(new Vector2Int(_x, _y), ROTATE_180);
                                    CalculateEdge(new Vector2Int(_x, _y), ROTATE_270);
                                }
                            }
                            _x++;
                            if (_x >= room.TileWidth)
                            {
                                _x = 0;
                                _y++;
                                if (_y >= room.TileHeight)
                                {
                                    _y = corners.Count;
                                    state = MappingState.DuplicatingPoints;
                                }
                            }
                        }
                        break;
                    case MappingState.DuplicatingPoints:
                        {
                            corners.Add(corners[_x]);
                            _x++;
                            if (_x >= _y)
                            {
                                state = MappingState.Done;
                                _x = 0;
                                _y = 0;
                            }
                        }
                        break;
                    case MappingState.Done:
                        return;
                }
            }
        }

        private bool HasEdge(int x, int y, Direction dir)
        {
            Room.Tile tile = room.GetTile(x, y);
            Room.Tile.TerrainType terrain = tile.Terrain;
            Room.SlopeDirection slope = (terrain == Room.Tile.TerrainType.Slope) ? room.IdentifySlope(x, y) : Room.SlopeDirection.Broken;

            if (terrain == Room.Tile.TerrainType.Solid) return true;
            if (terrain == Room.Tile.TerrainType.Air ||
                terrain == Room.Tile.TerrainType.ShortcutEntrance ||
                terrain == Room.Tile.TerrainType.Floor) return false;
            switch (dir)
            {
                case Direction.Up:
                    return slope == Room.SlopeDirection.DownRight || slope == Room.SlopeDirection.DownLeft;
                case Direction.Right:
                    return slope == Room.SlopeDirection.UpLeft || slope == Room.SlopeDirection.DownLeft;
                case Direction.Down:
                    return slope == Room.SlopeDirection.UpRight || slope == Room.SlopeDirection.UpLeft;
                case Direction.Left:
                    return slope == Room.SlopeDirection.DownRight || slope == Room.SlopeDirection.UpRight;
            }
            return false;
        }

        private int AddCorner(Vector2 pos)
        {
            int ind = corners.IndexOf(pos);
            if (ind == -1)
            {
                corners.Add(pos);
                ind = corners.Count - 1;
            }
            return ind;
        }

        private void AddEdge(int x, int y, Direction dir)
        {
            Vector2 mid = room.MiddleOfTile(x, y);
            int ind1 = -1;
            int ind2 = -1;
            switch (dir)
            {
                case Direction.Up:
                    ind1 = AddCorner(new Vector2(mid.x - 10f, mid.y + 10f));
                    ind2 = AddCorner(new Vector2(mid.x + 10f, mid.y + 10f));
                    break;
                case Direction.Right:
                    ind1 = AddCorner(new Vector2(mid.x + 10f, mid.y + 10f));
                    ind2 = AddCorner(new Vector2(mid.x + 10f, mid.y - 10f));
                    break;
                case Direction.Down:
                    ind1 = AddCorner(new Vector2(mid.x + 10f, mid.y - 10f));
                    ind2 = AddCorner(new Vector2(mid.x - 10f, mid.y - 10f));
                    break;
                case Direction.Left:
                    ind1 = AddCorner(new Vector2(mid.x - 10f, mid.y - 10f));
                    ind2 = AddCorner(new Vector2(mid.x - 10f, mid.y + 10f));
                    break;
            }
            edges.Add(ind1);
            edges.Add(ind2);
        }

        private void AddSlopeEdge(int x, int y, Room.SlopeDirection dir)
        {
            //Room.SlopeDirection dir = room.IdentifySlope(x, y);
            Vector2 vector = room.MiddleOfTile(x, y);
            int item1 = -1;
            int item2 = -1;
            switch ((int)dir)
            {
                case 0: //upleft
                    item1 = AddCorner(new Vector2(vector.x - tileSize, vector.y - 10f));
                    item2 = AddCorner(new Vector2(vector.x + 10f, vector.y + tileSize));
                    break;
                case 1: //upright
                    item2 = AddCorner(new Vector2(vector.x + tileSize, vector.y - 10f));
                    item1 = AddCorner(new Vector2(vector.x - 10f, vector.y + tileSize));
                    break;
                case 2: //downleft
                    item1 = AddCorner(new Vector2(vector.x + 10f, vector.y - tileSize));
                    item2 = AddCorner(new Vector2(vector.x - tileSize, vector.y + 10f));
                    break;
                case 3: //downright
                    item2 = AddCorner(new Vector2(vector.x - 10f, vector.y - tileSize));
                    item1 = AddCorner(new Vector2(vector.x + tileSize, vector.y + 10f));
                    break;
            }
            edges.Add(item1);
            edges.Add(item2);
        }

        public void CalculateEdge(Vector2Int tile, Matrix rotationMatrix)
        {
            // get the necessary tiles for the calculation
            Vector2Int leftTile = tile + rotationMatrix.Transform(new Vector2Int(-1, 0));
            Vector2Int topLeftTile = tile + rotationMatrix.Transform(new Vector2Int(-1, 1));
            Vector2Int topTile = tile + rotationMatrix.Transform(new Vector2Int(0, 1));
            Vector2Int topRightTile = tile + rotationMatrix.Transform(new Vector2Int(1, 1));
            Vector2Int rightTile = tile + rotationMatrix.Transform(new Vector2Int(1, 0));

            Vector2 mid = room.MiddleOfTile(tile.x, tile.y);
            List<Vector2> vertices = new List<Vector2>();
            
            if (IsSolid(leftTile) && !IsSolid(topLeftTile) && IsSolid(topTile)) // L shaped
            {
                if (IsSlope(leftTile) || IsSlope(topTile))
                {
                    vertices.Add(new Vector2(-10f, tileSize));
                    vertices.Add(new Vector2(-tileSize, 10f));
                }
                else
                {
                    vertices.Add(new Vector2(-10f, tileSize));
                    vertices.Add(new Vector2(-tileSize, tileSize));
                    vertices.Add(new Vector2(-tileSize, 10f));
                }
            }
            else if (!IsSolid(topTile))
            {
                if (IsSolid(leftTile))
                    vertices.Add(new Vector2(-10f, tileSize));
                else
                {
                    if (IsSolid(topLeftTile))
                        vertices.Add(new Vector2(-10f, 10f));
                    vertices.Add(new Vector2(-tileSize, tileSize));
                }
                if (IsSolid(rightTile))
                    vertices.Add(new Vector2(10f, tileSize));
                else
                {
                    vertices.Add(new Vector2(tileSize, tileSize));
                    if (IsSolid(topRightTile))
                        vertices.Add(new Vector2(10f, 10f));
                }
            }

            // rotate vertices, translate them to their block, and add them to the list
            Vector2[] vertices2 = vertices.ToArray();
            for (int i = 1; i < vertices2.Length; i++)
            {
                edges.Add(AddCorner(mid + rotationMatrix.Transform(vertices2[i-1])));
                edges.Add(AddCorner(mid + rotationMatrix.Transform(vertices2[i])));
            }
        }

        public bool IsSolid(Vector2Int tile)
        {
            Room.Tile.TerrainType terrain = room.GetTile(tile.x, tile.y).Terrain;

            if (terrain == Room.Tile.TerrainType.Solid ||
                (terrain == Room.Tile.TerrainType.Slope &&
                room.IdentifySlope(tile.x, tile.y) != Room.SlopeDirection.Broken)) 
                return true;
            return false;
        }

        public bool IsSlope(Vector2Int tile)
        {

            return tiles[tile.x, tile.y].Terrain == Room.Tile.TerrainType.Slope &&
                room.IdentifySlope(tile.x, tile.y) != Room.SlopeDirection.Broken;
        }
    }

    class Matrix
    {
        float m11, m12, m21, m22;
        public Matrix(float m11, float m12, float m21, float m22)
        {
            this.m11 = m11;
            this.m12 = m12;
            this.m21 = m21;
            this.m22 = m22;
        }

        public Vector2 Transform(Vector2 v)
        {
            Vector2 result = new Vector2();
            result.x = m11 * v.x + m12 * v.y;
            result.y = m21 * v.x + m22 * v.y;
            return result;
        }

        public Vector2Int Transform(Vector2Int v)
        {
            Vector2Int result = new Vector2Int();
            result.x = (int)Math.Round(m11 * v.x + m12 * v.y);
            result.y = (int)Math.Round(m21 * v.x + m22 * v.y);
            return result;
        }
    }
}
