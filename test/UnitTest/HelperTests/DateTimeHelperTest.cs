using System;

using Altinn.Platform.Storage.Helpers;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest
{
    public class DateTimeHelperTest
    {
        public static readonly object[][] TestData =
        {
            new object[] { "2023-01-01", new TimeSpan(0, 0, 0), 1 },
            new object[] { "2023-02-02T00:00:00", new TimeSpan(0, 0, 0), 2 },
            new object[] { "2023-03-03T00:04:12+01", new TimeSpan(23, 4, 12), 2 }, // UTC is day before
            new object[] { "2023-04-04T05:23:42+04", new TimeSpan(1, 23, 42), 4 },
            new object[] { "2023-07-19T06:55:44Z", new TimeSpan(6, 55, 44), 19 },
            new object[] { "2023-04-01T21:13:22.276", new TimeSpan(0, 21, 13, 22, 276), 1 },
            new object[] { "2023-08-03T19:51:37.654+02", new TimeSpan(0, 17, 51, 37, 654), 3 },
            new object[] { "2023-06-01T01:36:44+02", new TimeSpan(23, 36, 44), 31 }, // UTC is day before
            new object[] { "2023-04-30T23:17:32-02", new TimeSpan(1, 17, 32), 1 } // UTC is day after
        };

        [Theory]
        [MemberData(nameof(TestData))]
        public void ParseAndConvertToUniversalTimeTest(string dateString, TimeSpan expectedTimeOfDay, int expectedDay)
        {
            // Act
            DateTime date = DateTimeHelper.ParseAndConvertToUniversalTime(dateString);

            // Assert
            Assert.Equal(DateTimeKind.Utc, date.Kind);
            Assert.Equal(expectedTimeOfDay, date.TimeOfDay);
            Assert.Equal(expectedDay, date.Day);
        }
    }
}
