using Microsoft.EntityFrameworkCore;

namespace BarBrain.Api.Tests.Integration;

/// <summary>
/// Sprint 2 acceptance: "full DOB provably absent from DB (schema + assertion
/// test)" — Hard Rule 2 / ADR-010. Two attacks: (1) the schema cannot even
/// hold a birth DATE, (2) after a real signup, the full DOB string appears in
/// no row of any table.
/// </summary>
[Collection("postgres")]
public sealed class NoFullDobTests(PostgresFixture fixture) : IAsyncLifetime
{
    private IdentityTestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        if (!fixture.DockerAvailable) return;
        _harness = new IdentityTestHarness(fixture.ConnectionString);
        await _harness.CleanupIdentityDataAsync();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    [SkippableFact]
    public async Task Schema_has_no_column_capable_of_holding_a_birth_date()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        await using var db = _harness.CreateDb();

        // Any column in the whole schema whose name smells like birth/DOB…
        var suspicious = await db.Database.SqlQueryRaw<string>(
            """
            SELECT table_name || '.' || column_name || ':' || data_type AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND (column_name ILIKE '%birth%' OR column_name ILIKE '%dob%')
            """).ToListAsync();

        // …must be exactly the sanctioned integer year. Nothing else.
        Assert.Equal(["users.BirthYear:integer"], suspicious);
    }

    [SkippableFact]
    public async Task After_signup_the_full_dob_string_exists_nowhere_in_the_database()
    {
        Skip.IfNot(fixture.DockerAvailable, "Docker not available; integration test skipped.");
        const string dob = "1993-11-24"; // distinctive; year asserted below
        await _harness.SignupAsync(_harness.CreateClient(), "dob_probe", dob: dob);

        await using var db = _harness.CreateDb();

        // Serialize EVERY row of EVERY public table to text and grep for the
        // full DOB. Slow-and-total on purpose — this is the assertion test the
        // spec demands.
        var tables = await db.Database.SqlQueryRaw<string>(
            """
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            """).ToListAsync();

        foreach (var table in tables)
        {
            // Table names come from information_schema above, dob is a
            // compile-time constant — no injection surface in this test.
#pragma warning disable EF1002
            var hits = await db.Database.SqlQueryRaw<int>(
                $"""SELECT count(*)::int AS "Value" FROM "{table}" t WHERE row_to_json(t)::text LIKE '%{dob}%'""")
                .SingleAsync();
#pragma warning restore EF1002
            Assert.True(hits == 0, $"Full DOB found in table '{table}'");
        }

        // What we DO keep: the year and the attestation moment.
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserName == "dob_probe");
        Assert.Equal(1993, user.BirthYear);
        Assert.NotNull(user.AttestedAt);
    }
}
