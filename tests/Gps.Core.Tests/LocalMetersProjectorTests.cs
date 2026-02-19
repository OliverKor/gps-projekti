namespace Gps.Core.Tests;

public sealed class LocalMetersProjectorTests
{
    [Fact]
    public void Project_ReturnsZeroAtOrigin()
    {
        var projector = new LocalMetersProjector();
        projector.SetOriginIfUnset(62.7905840, 22.8185170);

        var projected = projector.Project(62.7905840, 22.8185170);

        Assert.Equal(0.0, projected.LatitudeMeters, 8);
        Assert.Equal(0.0, projected.LongitudeMeters, 8);
    }

    [Fact]
    public void Project_ReturnsPositiveLatitudeMetersWhenMovingNorth()
    {
        var projector = new LocalMetersProjector();
        projector.SetOriginIfUnset(62.7905840, 22.8185170);

        var projected = projector.Project(62.7915840, 22.8185170);

        Assert.InRange(projected.LatitudeMeters, 111.0, 112.0);
        Assert.Equal(0.0, projected.LongitudeMeters, 8);
    }

    [Fact]
    public void Project_ReturnsPositiveLongitudeMetersWhenMovingEast()
    {
        var projector = new LocalMetersProjector();
        projector.SetOriginIfUnset(62.7905840, 22.8185170);

        var projected = projector.Project(62.7905840, 22.8195170);

        Assert.Equal(0.0, projected.LatitudeMeters, 8);
        Assert.InRange(projected.LongitudeMeters, 50.0, 52.0);
    }

    [Fact]
    public void Project_ReturnsSmallerLongitudeDistanceAtHigherLatitude()
    {
        var lowLatitudeProjector = new LocalMetersProjector();
        lowLatitudeProjector.SetOriginIfUnset(0.0, 0.0);
        var lowLatitudeDistance = lowLatitudeProjector.Project(0.0, 0.01).LongitudeMeters;

        var highLatitudeProjector = new LocalMetersProjector();
        highLatitudeProjector.SetOriginIfUnset(70.0, 0.0);
        var highLatitudeDistance = highLatitudeProjector.Project(70.0, 0.01).LongitudeMeters;

        Assert.True(lowLatitudeDistance > highLatitudeDistance);
        Assert.True(highLatitudeDistance > 0);
    }
}
