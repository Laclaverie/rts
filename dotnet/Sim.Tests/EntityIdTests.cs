using RTS.Sim.Engine;

namespace RTS.Sim.Tests
{
    public class EntityIdTests
    {
        [Test]
        public void Ids_with_the_same_value_are_equal()
        {
            Assert.That(new EntityId(7), Is.EqualTo(new EntityId(7)));
            Assert.That(new EntityId(7), Is.Not.EqualTo(new EntityId(8)));
        }

        [Test]
        public void Default_is_none()
        {
            Assert.That(default(EntityId), Is.EqualTo(EntityId.None));
            Assert.That(default(EntityId).IsNone, Is.True);
            Assert.That(new EntityId(1).IsNone, Is.False);
        }
    }
}
