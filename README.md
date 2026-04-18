# Arcade Leaderboard API

## Container Image

This repo includes a GitHub Actions workflow that publishes the API container image to GitHub Container Registry (GHCR):

- Workflow: [`.github/workflows/publish-api-image.yml`](C:/Dev/Arcade-Leaderboard/.github/workflows/publish-api-image.yml)
- Dockerfile: [`ArcadeLeaderboard/Dockerfile`](C:/Dev/Arcade-Leaderboard/ArcadeLeaderboard/Dockerfile)

Once this project is pushed to GitHub, the published image name will be:

```text
ghcr.io/<github-owner>/<github-repo>
```

Examples:

```text
ghcr.io/acme/arcade-leaderboard
ghcr.io/justin/arcade-leaderboard
```

Typical tags created by the workflow:

- `latest` for the default branch
- `sha-<commit>` for each published commit
- `v1.0.0` style tags when you push a Git tag

## Terraform Reference

In Terraform, you can reference the published API image like this:

```hcl
leaderboard_image = "ghcr.io/<github-owner>/<github-repo>:latest"
```

For a pinned deployment, prefer a version or SHA tag:

```hcl
leaderboard_image = "ghcr.io/<github-owner>/<github-repo>:v1.0.0"
```

If the GHCR package is private, the deployment environment will also need to authenticate before it can pull the image.
