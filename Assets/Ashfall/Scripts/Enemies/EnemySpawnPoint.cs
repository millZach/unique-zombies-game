using UnityEngine;
using Ashfall.Core;
using Ashfall.Nav;

namespace Ashfall.Enemies
{
    /// <summary>
    /// One place enemies can enter the station from.
    ///
    /// Spawn points come online with map phases, which is how the pressure geometry of
    /// a round changes: at Standby everything comes from two courtyard breaches, by
    /// Meridian the player is being flanked from the roof.
    /// </summary>
    public class EnemySpawnPoint : MonoBehaviour
    {
        [Header("Availability")]
        [SerializeField] private MapPhase requiredPhase = MapPhase.Standby;
        [SerializeField] private StationZone zone = StationZone.Courtyard;
        [SerializeField] private bool requiresRouteOpen;
        [SerializeField] private string requiredGateName = "";

        [Header("Placement")]
        [SerializeField] private float spawnRadius = 1.2f;
        [SerializeField] private float minDistanceFromPlayer = 9f;

        [Header("Weighting")]
        [Tooltip("Relative chance of being chosen. Raise for the routes you want to be the story of the round.")]
        [SerializeField] private float weight = 1f;

        public MapPhase RequiredPhase => requiredPhase;
        public StationZone Zone => zone;
        public float Weight => Mathf.Max(0.01f, weight);
        public float MinDistanceFromPlayer => minDistanceFromPlayer;

        public bool PhaseUnlocked { get; private set; }

        public void Configure(MapPhase phase, StationZone stationZone, float pointWeight, bool needsRoute, string gateName)
        {
            requiredPhase = phase;
            zone = stationZone;
            weight = pointWeight;
            requiresRouteOpen = needsRoute;
            requiredGateName = gateName;
        }

        public void SetPhase(MapPhase phase)
        {
            PhaseUnlocked = phase >= requiredPhase;
        }

        /// <summary>True when this point may be used right now.</summary>
        public bool IsUsable(Vector3 playerPosition)
        {
            if (!PhaseUnlocked || !isActiveAndEnabled)
            {
                return false;
            }

            if (requiresRouteOpen && NavGraph.Active != null && !string.IsNullOrEmpty(requiredGateName))
            {
                if (!NavGraph.Active.IsGateOpen(NavGraph.Active.GateIdByName(requiredGateName)))
                {
                    return false;
                }
            }

            // Never pop an enemy into view right next to the player.
            return (transform.position - playerPosition).sqrMagnitude >= minDistanceFromPlayer * minDistanceFromPlayer;
        }

        public Vector3 SamplePosition()
        {
            Vector2 disc = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = transform.position + new Vector3(disc.x, 0f, disc.y);

            // Drop onto the floor so an imprecisely placed marker cannot spawn an enemy
            // hovering or buried.
            if (Physics.Raycast(candidate + Vector3.up * 2.5f, Vector3.down, out RaycastHit hit, 8f,
                    AshfallLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                candidate.y = hit.point.y + 0.05f;
            }

            return candidate;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Color c = requiredPhase switch
            {
                MapPhase.Standby => AshfallPalette.EmergencyAmber,
                MapPhase.Breach => AshfallPalette.HazardYellow,
                MapPhase.Surge => AshfallPalette.WarningRed,
                MapPhase.Blackout => AshfallPalette.StormTealDeep,
                _ => AshfallPalette.StormTeal
            };

            Gizmos.color = new Color(c.r, c.g, c.b, 0.65f);
            Gizmos.DrawWireSphere(transform.position, spawnRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
        }
#endif
    }
}
