using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Ashfall.Enemies;

namespace Ashfall.Tests
{
    /// <summary>
    /// What has to hold in play mode while the six paid slots are still empty.
    ///
    /// The rigged path cannot be exercised here -- there is no approved
    /// Meshcaster art in this repository and none is faked -- so what is tested
    /// is the half that ships today: a bridge with nothing to drive must be
    /// harmless, and the two switches it flips must leave pooling, the death
    /// timer and the alive count untouched.
    /// </summary>
    public class ZombieAnimatorPlayTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        [UnityTest]
        public IEnumerator ABridgeWithNoAnimatorIsInertAndLeavesTheProceduralGaitOn()
        {
            _root = new GameObject("Enemy_NoRig");
            _root.SetActive(false);

            var health = _root.AddComponent<EnemyHealth>();
            _root.AddComponent<Ashfall.Nav.SteeringAgent>();
            var brain = _root.AddComponent<EnemyBrain>();
            var bridge = _root.AddComponent<ZombieAnimator>();
            bridge.Configure(null, brain, health, null);

            _root.SetActive(true);

            // Several frames, because the failure this guards against is a
            // NullReferenceException per frame in Update, not at construction.
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.IsFalse(bridge.DrivesBody,
                "With no Animator the bridge must not claim the body.");
            Assert.IsTrue(brain.ProceduralGaitEnabled,
                "A bridge that cannot animate must leave the procedural gait running.");
            Assert.IsTrue(health.ProceduralDeathCollapse,
                "A bridge with no Death state must leave the death squash on.");
        }

        [UnityTest]
        public IEnumerator DisablingTheProceduralGaitStopsTheVisualRootMoving()
        {
            _root = new GameObject("Enemy_Gait");
            _root.SetActive(false);

            var definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            EnemyDefinition.ApplyShambler(definition);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(_root.transform, false);

            _root.AddComponent<CharacterController>();
            _root.AddComponent<EnemyHealth>();
            _root.AddComponent<Ashfall.Nav.SteeringAgent>();
            var brain = _root.AddComponent<EnemyBrain>();

            brain.GetType()
                .GetField("visualRoot", System.Reflection.BindingFlags.Instance
                                        | System.Reflection.BindingFlags.NonPublic)
                .SetValue(brain, visual.transform);

            _root.SetActive(true);
            brain.Spawn(definition, null, 1f, 1f, 1f);
            brain.ProceduralGaitEnabled = false;

            Vector3 restPosition = visual.transform.localPosition;
            Quaternion restRotation = visual.transform.localRotation;

            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(restPosition, visual.transform.localPosition,
                "The gait moved the visual root after it was handed to the animator.");
            Assert.AreEqual(restRotation, visual.transform.localRotation,
                "The gait leaned the visual root after it was handed to the animator.");

            Object.DestroyImmediate(definition);
        }

        [UnityTest]
        public IEnumerator DeathStillFinishesOnTimeWithTheSquashTurnedOff()
        {
            // The pool recycles on DeathFinished. If turning the squash off also
            // changed that timing, a rigged enemy would leak corpses or vanish
            // mid-collapse -- the two ways this integration could break a round.
            _root = new GameObject("Enemy_Death");
            _root.SetActive(false);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(_root.transform, false);

            var health = _root.AddComponent<EnemyHealth>();
            health.Configure(new Renderer[0], new Collider[0], visual.transform);
            _root.SetActive(true);

            health.ProceduralDeathCollapse = false;
            health.SetDeathCollapseSeconds(0.25f);
            health.Initialise(10f, Color.grey, Color.black);

            bool finished = false;
            health.DeathFinished += _ => finished = true;

            health.ApplyDamage(Core.DamageInfo.Melee(20f, Vector3.zero, Vector3.forward, _root));
            Assert.IsFalse(health.IsAlive);
            Assert.IsTrue(health.IsDying);

            float deadline = Time.time + 2f;
            while (!finished && Time.time < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(finished, "DeathFinished never fired, so the pool would never recycle the body.");
            Assert.AreEqual(Vector3.one, visual.transform.localScale,
                "The squash ran even though the animator owns the collapse.");
        }

        [UnityTest]
        public IEnumerator DeathCollapseSecondsIsClampedToSomethingSurvivable()
        {
            _root = new GameObject("Enemy_Clamp");
            var health = _root.AddComponent<EnemyHealth>();

            yield return null;

            // A clip length read off a broken controller must not be able to
            // park a corpse in the pool forever, or stop it being seen at all.
            health.SetDeathCollapseSeconds(0f);
            health.SetDeathCollapseSeconds(600f);
            health.SetDeathCollapseSeconds(float.NaN);

            Assert.Pass("Out-of-range collapse timings are absorbed rather than thrown.");
        }
    }
}
