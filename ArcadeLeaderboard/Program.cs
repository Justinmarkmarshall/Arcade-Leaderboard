using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ScoreRepository>();

var app = builder.Build();
//test change
await using (var scope = app.Services.CreateAsyncScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<ScoreRepository>();
    await repository.EnsureTableExistsAsync();
}

app.MapPost("/scores", async (CreateScoreRequest request, ScoreRepository repository, CancellationToken cancellationToken) =>
{
    var createdScore = await repository.CreateAsync(request, cancellationToken);
    return Results.Created($"/scores/{createdScore.Game}", createdScore);
});

app.MapGet("/scores", async (ScoreRepository repository, CancellationToken cancellationToken) =>
{
    var scores = await repository.GetAllAsync(cancellationToken);
    return Results.Ok(scores);
});

app.MapGet("/scores/{game}", async (string game, ScoreRepository repository, CancellationToken cancellationToken) =>
{
    var scores = await repository.GetByGameAsync(game, cancellationToken);
    return Results.Ok(scores);
});

app.MapGet("/scores/{game}/top/{limit:int}", async (string game, int limit, ScoreRepository repository, CancellationToken cancellationToken) =>
{
    if (limit <= 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["limit"] = ["Limit must be greater than zero."]
        });
    }

    var scores = await repository.GetTopByGameAsync(game, limit, cancellationToken);
    return Results.Ok(scores);
});

app.MapDelete("/scores/{id:guid}", async (Guid id, ScoreRepository repository, CancellationToken cancellationToken) =>
{
    var deleted = await repository.DeleteAsync(id, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.Run();

internal sealed class ScoreRepository(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("LeaderboardDb")
        ?? throw new InvalidOperationException("ConnectionStrings:LeaderboardDb is not configured.");

    public async Task EnsureTableExistsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS scores (
                id UUID PRIMARY KEY,
                player_id TEXT NOT NULL,
                player_name TEXT NOT NULL,
                game TEXT NOT NULL,
                score INT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LeaderboardScore> CreateAsync(CreateScoreRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO scores (id, player_id, player_name, game, score, created_at)
            VALUES (@id, @playerId, @playerName, @game, @score, @createdAt)
            RETURNING id, player_id, player_name, game, score, created_at;
            """;

        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("playerId", request.PlayerId);
        command.Parameters.AddWithValue("playerName", request.PlayerName);
        command.Parameters.AddWithValue("game", request.Game);
        command.Parameters.AddWithValue("score", request.Score);
        command.Parameters.AddWithValue("createdAt", createdAt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return MapScore(reader);
    }

    public async Task<IReadOnlyList<LeaderboardScore>> GetAllAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, player_id, player_name, game, score, created_at
            FROM scores
            ORDER BY created_at DESC, score DESC;
            """;

        return await QueryScoresAsync(sql, cancellationToken);
    }

    public async Task<IReadOnlyList<LeaderboardScore>> GetByGameAsync(string game, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, player_id, player_name, game, score, created_at
            FROM scores
            WHERE game = @game
            ORDER BY score DESC, created_at ASC;
            """;

        return await QueryScoresAsync(
            sql,
            cancellationToken,
            parameters => parameters.AddWithValue("game", game));
    }

    public async Task<IReadOnlyList<LeaderboardScore>> GetTopByGameAsync(string game, int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, player_id, player_name, game, score, created_at
            FROM scores
            WHERE game = @game
            ORDER BY score DESC, created_at ASC
            LIMIT @limit;
            """;

        return await QueryScoresAsync(
            sql,
            cancellationToken,
            parameters =>
            {
                parameters.AddWithValue("game", game);
                parameters.AddWithValue("limit", limit);
            });
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM scores
            WHERE id = @id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        var deletedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return deletedRows > 0;
    }

    private async Task<IReadOnlyList<LeaderboardScore>> QueryScoresAsync(
        string sql,
        CancellationToken cancellationToken,
        Action<NpgsqlParameterCollection>? addParameters = null)
    {
        var scores = new List<LeaderboardScore>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        addParameters?.Invoke(command.Parameters);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            scores.Add(MapScore(reader));
        }

        return scores;
    }

    private static LeaderboardScore MapScore(NpgsqlDataReader reader)
    {
        return new LeaderboardScore(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }
}

internal sealed record CreateScoreRequest(string PlayerId, string PlayerName, string Game, int Score);

internal sealed record LeaderboardScore(
    Guid Id,
    string PlayerId,
    string PlayerName,
    string Game,
    int Score,
    DateTimeOffset CreatedAt);
