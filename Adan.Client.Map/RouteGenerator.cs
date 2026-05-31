namespace Adan.Client.Map
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Xml.Serialization;
    using Model;
    using SharpAESCrypt;

    /// <summary>
    /// Result of auto route generation.
    /// </summary>
    public class RouteGenerationResult
    {
        public int OriginalRouteCount { get; set; }
        public int NewRouteCount { get; set; }
        public int ZonesTotal { get; set; }
        public int ZonesCoveredBefore { get; set; }
        public int ZonesNewlyCovered { get; set; }
        public int ZonesUncovered { get; set; }
        public List<Route> AllRoutes { get; set; }
    }

    /// <summary>
    /// Generates routes for all zones using BFS over the room graph.
    /// Zones not reachable from the main hub are handled as isolated components,
    /// each getting their own local hub.
    /// </summary>
    public static class RouteGenerator
    {
        private const string AesPassword = "A5Ub5T7j5cYg40v";
        private const int HubRoomId = 10035;
        private const string HubDefaultName = "мм+";

        private static readonly XmlSerializer ZoneSerializer = new XmlSerializer(typeof(Zone));

        public static RouteGenerationResult Generate(string zonesDir, IList<Route> existingRoutes)
        {
            // Build graph from zone files
            var graph = new Dictionary<int, List<int>>();          // roomId -> exits
            var zoneNames = new Dictionary<int, string>();         // zoneId -> name
            var zoneRooms = new Dictionary<int, List<int>>();      // zoneId -> roomIds
            var roomToZones = new Dictionary<int, List<int>>();    // roomId -> zoneIds

            LoadZones(zonesDir, graph, zoneNames, zoneRooms, roomToZones);

            // Determine which zones are already covered by existing routes
            var namedPoints = new Dictionary<string, int>();  // name -> roomId
            var existingPairs = new HashSet<string>();        // "A|B" sorted

            foreach (var route in existingRoutes)
            {
                if (route.RouteRoomIdentifiers.Count == 0) continue;
                namedPoints[route.StartName] = route.StartRoomId;
                namedPoints[route.EndName] = route.EndRoomId;
                existingPairs.Add(MakePairKey(route.StartName, route.EndName));
            }

            var globallyCovered = new HashSet<int>();
            foreach (var rid in namedPoints.Values)
                foreach (var zid in GetZones(rid, roomToZones))
                    globallyCovered.Add(zid);

            foreach (var zid in GetZones(HubRoomId, roomToZones))
                globallyCovered.Add(zid);

            int zonesCoveredBefore = globallyCovered.Count;

            var usedNames = new HashSet<string>(namedPoints.Keys) { HubDefaultName };
            var newRoutes = new List<Route>();

            // Pass 1: expand from global hub
            string hubName = namedPoints.ContainsValue(HubRoomId)
                ? namedPoints.First(kv => kv.Value == HubRoomId).Key
                : HubDefaultName;

            var hubZones = GetZones(HubRoomId, roomToZones);
            if (hubZones.Count > 0)
            {
                var seedZone = hubZones[0];
                var pass1Routes = ExpandFromHub(seedZone, HubRoomId, hubName,
                    zoneNames.Keys.ToHashSet(), graph, zoneNames, zoneRooms, roomToZones,
                    existingPairs, usedNames, namedPoints, globallyCovered);
                newRoutes.AddRange(pass1Routes);
            }

            // Pass 2: isolated components
            var remaining = zoneNames.Keys.Where(z => !globallyCovered.Contains(z)).ToHashSet();
            if (remaining.Count > 0)
            {
                var zoneGraph = BuildZoneGraph(zoneNames.Keys, zoneRooms, roomToZones, graph);
                var components = FindComponents(remaining, zoneGraph);

                foreach (var component in components)
                {
                    int localHubZone = PickLocalHub(component, zoneGraph);
                    var hubRooms = zoneRooms.ContainsKey(localHubZone) ? zoneRooms[localHubZone] : new List<int>();
                    if (hubRooms.Count == 0) continue;

                    var componentRooms = new HashSet<int>(component.SelectMany(z => zoneRooms.ContainsKey(z) ? zoneRooms[z] : Enumerable.Empty<int>()));
                    int localHubRoom = hubRooms
                        .OrderByDescending(r => graph.ContainsKey(r) ? graph[r].Count(d => componentRooms.Contains(d)) : 0)
                        .First();

                    string localHubName = zoneNames.ContainsKey(localHubZone) ? zoneNames[localHubZone] : $"zone_{localHubZone}";
                    if (usedNames.Contains(localHubName))
                        localHubName = localHubName + " (хаб)";
                    usedNames.Add(localHubName);
                    namedPoints[localHubName] = localHubRoom;

                    var compRoutes = ExpandFromHub(localHubZone, localHubRoom, localHubName,
                        component, graph, zoneNames, zoneRooms, roomToZones,
                        existingPairs, usedNames, namedPoints, globallyCovered);
                    newRoutes.AddRange(compRoutes);
                }
            }

            int zonesUncovered = zoneNames.Keys.Count(z => !globallyCovered.Contains(z));

            var allRoutes = existingRoutes.ToList();
            allRoutes.AddRange(newRoutes);

            return new RouteGenerationResult
            {
                OriginalRouteCount = existingRoutes.Count,
                NewRouteCount = newRoutes.Count,
                ZonesTotal = zoneNames.Count,
                ZonesCoveredBefore = zonesCoveredBefore,
                ZonesNewlyCovered = globallyCovered.Count - zonesCoveredBefore,
                ZonesUncovered = zonesUncovered,
                AllRoutes = allRoutes,
            };
        }

        // ── Zone loading ─────────────────────────────────────────────────────────

        private static void LoadZones(string zonesDir,
            Dictionary<int, List<int>> graph,
            Dictionary<int, string> zoneNames,
            Dictionary<int, List<int>> zoneRooms,
            Dictionary<int, List<int>> roomToZones)
        {
            if (!Directory.Exists(zonesDir)) return;

            foreach (var file in Directory.GetFiles(zonesDir, "*.xml"))
            {
                try
                {
                    Zone zone;
                    using (var inStream = File.OpenRead(file))
                    {
                        var aes = new SharpAESCrypt(AesPassword, inStream, OperationMode.Decrypt);
                        using (var reader = new StreamReader(aes, Encoding.GetEncoding(20866)))
                            zone = (Zone)ZoneSerializer.Deserialize(reader);
                    }

                    zoneNames[zone.Id] = zone.Name;
                    if (!zoneRooms.ContainsKey(zone.Id))
                        zoneRooms[zone.Id] = new List<int>();

                    foreach (var room in zone.Rooms)
                    {
                        zoneRooms[zone.Id].Add(room.Id);

                        if (!roomToZones.ContainsKey(room.Id))
                            roomToZones[room.Id] = new List<int>();
                        roomToZones[room.Id].Add(zone.Id);

                        if (!graph.ContainsKey(room.Id))
                            graph[room.Id] = new List<int>();
                        foreach (var exit in room.Exits)
                            graph[room.Id].Add(exit.RoomId);
                    }
                }
                catch { /* skip broken zone files */ }
            }
        }

        // ── BFS ──────────────────────────────────────────────────────────────────

        private static List<int> BfsPath(Dictionary<int, List<int>> graph, int start, int end)
        {
            if (start == end) return new List<int> { start };
            if (!graph.ContainsKey(start)) return null;

            var parent = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (cur == end) break;
                if (!graph.ContainsKey(cur)) continue;
                foreach (int next in graph[cur])
                {
                    if (!parent.ContainsKey(next))
                    {
                        parent[next] = cur;
                        queue.Enqueue(next);
                    }
                }
            }

            if (!parent.ContainsKey(end)) return null;

            var path = new List<int>();
            int c = end;
            while (c != -1) { path.Add(c); c = parent[c]; }
            path.Reverse();
            return path;
        }

        // ── Wave expansion ───────────────────────────────────────────────────────

        private static List<Route> ExpandFromHub(
            int hubZone, int hubRoom, string hubName,
            HashSet<int> allZones,
            Dictionary<int, List<int>> graph,
            Dictionary<int, string> zoneNames,
            Dictionary<int, List<int>> zoneRooms,
            Dictionary<int, List<int>> roomToZones,
            HashSet<string> existingPairs,
            HashSet<string> usedNames,
            Dictionary<string, int> namedPoints,
            HashSet<int> globallyCovered)
        {
            var coveredZones = new HashSet<int> { hubZone };
            globallyCovered.Add(hubZone);
            var newRoutes = new List<Route>();

            while (true)
            {
                var coveredRooms = new HashSet<int>(
                    coveredZones.SelectMany(z => zoneRooms.ContainsKey(z) ? zoneRooms[z] : Enumerable.Empty<int>()));

                // Find candidate zones: uncovered zones reachable from covered rooms
                var candidates = new Dictionary<int, (int doorstep, int entry)>();
                foreach (int rid in coveredRooms)
                {
                    if (!graph.ContainsKey(rid)) continue;
                    foreach (int dst in graph[rid])
                    {
                        foreach (int zid in GetZones(dst, roomToZones))
                        {
                            if (allZones.Contains(zid) && !coveredZones.Contains(zid) && !candidates.ContainsKey(zid))
                                candidates[zid] = (rid, dst);
                        }
                    }
                }

                if (candidates.Count == 0) break;

                bool foundAny = false;
                foreach (var kv in candidates)
                {
                    int zid = kv.Key;
                    int doorstep = kv.Value.doorstep;

                    string zoneName = zoneNames.ContainsKey(zid) ? zoneNames[zid] : $"zone_{zid}";

                    if (usedNames.Contains(zoneName))
                    {
                        int existingRid;
                        bool sameZone = namedPoints.TryGetValue(zoneName, out existingRid)
                            && GetZones(existingRid, roomToZones).Contains(zid);
                        if (sameZone) { coveredZones.Add(zid); globallyCovered.Add(zid); continue; }
                        zoneName = zoneName + " (зона)";
                    }

                    string pairKey = MakePairKey(zoneName, hubName);
                    if (existingPairs.Contains(pairKey))
                    {
                        coveredZones.Add(zid); globallyCovered.Add(zid); continue;
                    }

                    var path = BfsPath(graph, doorstep, hubRoom);
                    if (path != null)
                    {
                        var route = new Route { StartName = zoneName, EndName = hubName };
                        route.RouteRoomIdentifiers.AddRange(path);
                        newRoutes.Add(route);
                        namedPoints[zoneName] = doorstep;
                        usedNames.Add(zoneName);
                        existingPairs.Add(pairKey);
                        foundAny = true;
                    }

                    coveredZones.Add(zid);
                    globallyCovered.Add(zid);
                }

                if (!foundAny) break;
            }

            return newRoutes;
        }

        // ── Zone graph & components ───────────────────────────────────────────────

        private static Dictionary<int, HashSet<int>> BuildZoneGraph(
            IEnumerable<int> allZoneIds,
            Dictionary<int, List<int>> zoneRooms,
            Dictionary<int, List<int>> roomToZones,
            Dictionary<int, List<int>> graph)
        {
            var zg = new Dictionary<int, HashSet<int>>();
            foreach (int z in allZoneIds) zg[z] = new HashSet<int>();

            foreach (var kv in graph)
            {
                int rid = kv.Key;
                var srcZones = GetZones(rid, roomToZones);
                foreach (int dst in kv.Value)
                {
                    var dstZones = GetZones(dst, roomToZones);
                    foreach (int sz in srcZones)
                        foreach (int dz in dstZones)
                            if (sz != dz && zg.ContainsKey(sz))
                                zg[sz].Add(dz);
                }
            }
            return zg;
        }

        private static List<HashSet<int>> FindComponents(HashSet<int> seeds, Dictionary<int, HashSet<int>> zoneGraph)
        {
            var visited = new HashSet<int>();
            var components = new List<HashSet<int>>();
            foreach (int z in seeds)
            {
                if (visited.Contains(z)) continue;
                var comp = new HashSet<int>();
                var q = new Queue<int>();
                q.Enqueue(z);
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    if (visited.Contains(cur)) continue;
                    visited.Add(cur); comp.Add(cur);
                    if (!zoneGraph.ContainsKey(cur)) continue;
                    foreach (int nb in zoneGraph[cur])
                        if (seeds.Contains(nb) && !visited.Contains(nb))
                            q.Enqueue(nb);
                }
                components.Add(comp);
            }
            return components;
        }

        private static int PickLocalHub(HashSet<int> component, Dictionary<int, HashSet<int>> zoneGraph)
        {
            return component.OrderByDescending(z =>
                zoneGraph.ContainsKey(z) ? zoneGraph[z].Count(nb => component.Contains(nb)) : 0
            ).First();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static List<int> GetZones(int roomId, Dictionary<int, List<int>> roomToZones)
        {
            return roomToZones.ContainsKey(roomId) ? roomToZones[roomId] : new List<int>();
        }

        private static string MakePairKey(string a, string b)
        {
            return string.Compare(a, b, StringComparison.Ordinal) < 0 ? a + "|" + b : b + "|" + a;
        }
    }
}
