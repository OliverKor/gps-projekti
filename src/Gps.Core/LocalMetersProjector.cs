namespace Gps.Core;

internal sealed class LocalMetersProjector
{
    private const double MetersPerLatitudeDegree = 111_132.92;
    private const double MetersPerLongitudeDegreeAtEquator = 111_320.0;

    private double? _originLatitudeDeg;
    private double? _originLongitudeDeg;
    private double _cosOriginLatitude;

    public void SetOriginIfUnset(double latitudeDeg, double longitudeDeg)
    {
        if (_originLatitudeDeg.HasValue)
        {
            return;
        }

        _originLatitudeDeg = latitudeDeg;
        _originLongitudeDeg = longitudeDeg;
        _cosOriginLatitude = Math.Cos(latitudeDeg * (Math.PI / 180.0));
    }

    public (double LatitudeMeters, double LongitudeMeters) Project(double latitudeDeg, double longitudeDeg)
    {
        SetOriginIfUnset(latitudeDeg, longitudeDeg);

        if (!_originLatitudeDeg.HasValue || !_originLongitudeDeg.HasValue)
        {
            throw new InvalidOperationException("Projection origin is not initialized.");
        }

        var latitudeMeters = (latitudeDeg - _originLatitudeDeg.Value) * MetersPerLatitudeDegree;
        var longitudeMeters = (longitudeDeg - _originLongitudeDeg.Value) * MetersPerLongitudeDegreeAtEquator * _cosOriginLatitude;
        return (latitudeMeters, longitudeMeters);
    }
}
