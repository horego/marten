using System;
using System.Text.RegularExpressions;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Services;

internal class EventStreamUnexpectedMaxEventIdExceptionTransform: IExceptionTransform
{
    private const string ExpectedMessage =
        "duplicate key value violates unique constraint \"pk_mt_events_stream_and_version\"";

    private const string StreamId = "<streamid>";
    private const string Version = "<version>";

    [Obsolete("let's get rid of this")]
    public static readonly EventStreamUnexpectedMaxEventIdExceptionTransform Instance = new();

    private static readonly Regex EventStreamUniqueExceptionDetailsRegex =
        new(@"^Key \(stream_id, version\)=\((?<streamid>.*?), (?<version>\w+)\)");

    public bool TryTransform(Exception original, out Exception transformed)
    {
        if (!Matches(original))
        {
            transformed = null;
            return false;
        }

        var postgresException = (PostgresException)original.InnerException;

        object id = null;
        Type aggregateType = null;
        var expected = -1;
        var actual = -1;

        if (!string.IsNullOrEmpty(postgresException.Detail))
        {
            var details = EventStreamUniqueExceptionDetailsRegex.Match(postgresException.Detail);

            if (details.Groups[StreamId].Success)
            {
                var streamId = details.Groups[StreamId].Value;

                id = Guid.TryParse(streamId, out var guidStreamId) ? guidStreamId : streamId;
            }

            if (details.Groups[Version].Success)
            {
                var actualVersion = details.Groups[Version].Value;

                if (int.TryParse(actualVersion, out var actualIntVersion))
                {
                    actual = actualIntVersion;
                    expected = actual - 1;
                }
            }
        }

        transformed = new EventStreamUnexpectedMaxEventIdException(id, aggregateType, expected, actual);
        return true;
    }

    private static bool Matches(Exception e)
    {
        return e?.InnerException is PostgresException pe
               && pe.SqlState == PostgresErrorCodes.UniqueViolation
               && pe.Message.Contains(ExpectedMessage);
    }
}
