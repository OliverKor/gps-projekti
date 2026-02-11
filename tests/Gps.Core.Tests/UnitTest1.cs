using Gps.Core;

namespace Gps.Core.Tests;

public class CsvFixReaderTests
{
    [Fact]
    public void Read_ParsesValidRowsAndOptionalColumns()
    {
        var path = CreateTempCsv(
            "timestamp,lat,lon,speed_mps,num_sv,fix_type,lat_m,lon_m\n" +
            "2026-02-04T14:15:06.0000000+00:00,62.7905840,22.8185170,0.05,6,3D,0.00,0.00\n" +
            "2026-02-04T14:15:07.0000000+00:00,62.7905900,22.8185200,,,,,\n");

        try
        {
            var fixes = CsvFixReader.Read(path);

            Assert.Equal(2, fixes.Count);
            Assert.Equal(62.7905840, fixes[0].LatitudeDeg, 7);
            Assert.Equal(22.8185170, fixes[0].LongitudeDeg, 7);
            Assert.Equal(0.05, fixes[0].SpeedMps);
            Assert.Equal(6, fixes[0].NumSv);
            Assert.Equal("3D", fixes[0].FixType);
            Assert.NotNull(fixes[0].LatitudeMeters);
            Assert.Equal(0.00, fixes[0].LatitudeMeters!.Value, 2);
            Assert.NotNull(fixes[0].LongitudeMeters);
            Assert.Equal(0.00, fixes[0].LongitudeMeters!.Value, 2);

            Assert.Null(fixes[1].SpeedMps);
            Assert.Null(fixes[1].NumSv);
            Assert.Null(fixes[1].FixType);
            Assert.Null(fixes[1].LatitudeMeters);
            Assert.Null(fixes[1].LongitudeMeters);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_SkipsInvalidRows()
    {
        var path = CreateTempCsv(
            "timestamp,lat,lon,speed_mps,num_sv,fix_type,lat_m,lon_m\n" +
            "invalid,62.7905840,22.8185170,0.05,6,3D\n" +
            "2026-02-04T14:15:07.0000000+00:00,not-a-number,22.8185200,,,\n" +
            "2026-02-04T14:15:08.0000000+00:00,62.7906000,22.8185300,0.20,7,3D,1.78,0.72\n");

        try
        {
            var fixes = CsvFixReader.Read(path);

            Assert.Single(fixes);
            Assert.Equal(62.7906000, fixes[0].LatitudeDeg, 7);
            Assert.Equal(22.8185300, fixes[0].LongitudeDeg, 7);
            Assert.Equal(0.20, fixes[0].SpeedMps);
            Assert.Equal(7, fixes[0].NumSv);
            Assert.Equal("3D", fixes[0].FixType);
            Assert.NotNull(fixes[0].LatitudeMeters);
            Assert.Equal(1.78, fixes[0].LatitudeMeters!.Value, 2);
            Assert.NotNull(fixes[0].LongitudeMeters);
            Assert.Equal(0.72, fixes[0].LongitudeMeters!.Value, 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempCsv(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }
}
