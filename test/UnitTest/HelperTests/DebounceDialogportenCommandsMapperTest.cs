using System;
using Altinn.Platform.Storage.Helpers;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests
{
    public class DebounceDialogportenCommandsMapperTest
    {
        [Fact]
        public void SameInputs_ProduceSameGuid_AndBucketEnd()
        {
            var now = new DateTimeOffset(2025, 8, 31, 12, 0, 3, TimeSpan.Zero);
            var bucket = TimeSpan.FromSeconds(5);
            var resourceId = "instance-123";

            var (id1, end1) = DebounceDialogportenCommandsMapper.TimeBucketId(resourceId, bucket, now);
            var (id2, end2) = DebounceDialogportenCommandsMapper.TimeBucketId(resourceId, bucket, now);

            Assert.Equal(id1, id2);
            Assert.Equal(end1, end2);
        }

        [Theory]
        [InlineData("2025-08-31T12:00:00Z", 5, "2025-08-31T12:00:05Z")]
        [InlineData("2025-08-31T12:00:04Z", 5, "2025-08-31T12:00:05Z")]
        [InlineData("2025-08-31T12:00:05Z", 5, "2025-08-31T12:00:10Z")]
        [InlineData("2025-08-31T12:00:09.9990000Z", 5, "2025-08-31T12:00:10Z")]
        public void BucketEnd_IsAligned_ToBucketBoundary(string nowIso, int bucketSeconds, string expectedEndIso)
        {
            var now = DateTimeOffset.Parse(nowIso);
            var bucket = TimeSpan.FromSeconds(bucketSeconds);

            var (_, end) = DebounceDialogportenCommandsMapper.TimeBucketId("r", bucket, now);

            Assert.Equal(DateTimeOffset.Parse(expectedEndIso), end);
        }

        [Fact]
        public void DifferentResourceIds_ProduceDifferentGuids_WithinSameBucket()
        {
            var now = DateTimeOffset.Parse("2025-08-31T12:34:56Z");
            var bucket = TimeSpan.FromSeconds(5);

            var (idA, endA) = DebounceDialogportenCommandsMapper.TimeBucketId("A", bucket, now);
            var (idB, endB) = DebounceDialogportenCommandsMapper.TimeBucketId("B", bucket, now);

            Assert.NotEqual(idA, idB);
            Assert.Equal(endA, endB); // same bucket boundary
        }

        [Fact]
        public void DifferentBucketSizes_AffectGuidAndEnd()
        {
            var now = DateTimeOffset.Parse("2025-08-31T12:00:04Z");

            var (id5, end5) = DebounceDialogportenCommandsMapper.TimeBucketId("r", TimeSpan.FromSeconds(5),  now);
            var (id10, end10) = DebounceDialogportenCommandsMapper.TimeBucketId("r", TimeSpan.FromSeconds(10), now);

            Assert.NotEqual(id5, id10);
            Assert.Equal(DateTimeOffset.Parse("2025-08-31T12:00:05Z"), end5);
            Assert.Equal(DateTimeOffset.Parse("2025-08-31T12:00:10Z"), end10);
        }

        [Fact]
        public void CrossingBucketBoundary_ChangesGuid_AndBucketEnd()
        {
            var bucket = TimeSpan.FromSeconds(5);
            var resource = "r";
            var justBefore = DateTimeOffset.Parse("2025-08-31T12:00:04.9990000Z");
            var justAfter = DateTimeOffset.Parse("2025-08-31T12:00:05.0000000Z");

            var (idBefore, endBefore) = DebounceDialogportenCommandsMapper.TimeBucketId(resource, bucket, justBefore);
            var (idAfter,  endAfter) = DebounceDialogportenCommandsMapper.TimeBucketId(resource, bucket, justAfter);

            Assert.NotEqual(idBefore, idAfter);
            Assert.NotEqual(endBefore, endAfter);
            Assert.Equal(DateTimeOffset.Parse("2025-08-31T12:00:05Z"), endBefore);
            Assert.Equal(DateTimeOffset.Parse("2025-08-31T12:00:10Z"), endAfter);
        }

        [Fact]
        public void UsesUtcForBucketMath_IgnoringLocalOffset()
        {
            // Oslo summer time (UTC+2). Internally the method constructs bucketStart with offset 0.
            var nowLocal = new DateTimeOffset(2025, 8, 31, 14, 0, 4, TimeSpan.FromHours(2));
            var bucket = TimeSpan.FromSeconds(5);

            var (_, end) = DebounceDialogportenCommandsMapper.TimeBucketId("r", bucket, nowLocal);

            // Expect alignment based on UTC ticks: 14:00:04+02:00 == 12:00:04Z → end at 12:00:05Z
            Assert.Equal(DateTimeOffset.Parse("2025-08-31T12:00:05Z"), end);
        }
    }
}
