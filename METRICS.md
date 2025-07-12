# Dai-Lete Metrics and Monitoring

## Overview

Dai-Lete now includes OpenTelemetry metrics to track the amount of time saved by removing ads from podcast episodes. These metrics can be scraped by Prometheus and visualized in Grafana.

## Metrics Exposed

### `podcast_time_saved_seconds_total`
- **Type**: Counter
- **Description**: Total time saved by removing ads from podcasts
- **Unit**: seconds
- **Labels**:
  - `podcast_id`: Unique identifier for the podcast
  - `podcast_name`: Human-readable name of the podcast
  - `episode_id`: Unique identifier for the episode

### `podcast_time_saved_seconds`
- **Type**: Histogram
- **Description**: Time saved per episode by removing ads
- **Unit**: seconds
- **Labels**: Same as above

## Prometheus Configuration

Add the following to your `prometheus.yml`:

```yaml
scrape_configs:
  - job_name: 'dai-lete'
    static_configs:
      - targets: ['localhost:5000']  # Adjust port as needed
    metrics_path: '/metrics'
    scrape_interval: 30s
```

## Grafana Dashboard

### Panel: Weekly Time Saved by Podcast

**Query**:
```promql
sum by (podcast_name) (increase(podcast_time_saved_seconds_total[1w])) / 60
```

**Panel Settings**:
- Visualization: Table or Bar Chart
- Unit: minutes
- Title: "Time Saved This Week (Minutes)"

### Panel: Total Time Saved by Podcast

**Query**:
```promql
sum by (podcast_name) (podcast_time_saved_seconds_total) / 60
```

**Panel Settings**:
- Visualization: Stat or Bar Chart
- Unit: minutes
- Title: "Total Time Saved (Minutes)"

### Panel: Average Time Saved per Episode

**Query**:
```promql
avg by (podcast_name) (podcast_time_saved_seconds) / 60
```

**Panel Settings**:
- Visualization: Stat
- Unit: minutes
- Title: "Average Time Saved per Episode (Minutes)"

### Panel: Time Saved Over Time

**Query**:
```promql
sum(rate(podcast_time_saved_seconds_total[5m])) * 60
```

**Panel Settings**:
- Visualization: Time Series
- Unit: minutes/minute
- Title: "Time Saved Rate (Minutes per Minute)"

## Example Dashboard JSON

```json
{
  "dashboard": {
    "title": "Dai-Lete Podcast Metrics",
    "panels": [
      {
        "title": "Weekly Time Saved by Podcast",
        "type": "table",
        "targets": [
          {
            "expr": "sum by (podcast_name) (increase(podcast_time_saved_seconds_total[1w])) / 60",
            "format": "table"
          }
        ],
        "fieldConfig": {
          "defaults": {
            "unit": "m"
          }
        }
      }
    ]
  }
}
```

## Metrics Endpoint Security

The metrics endpoint at `/metrics` is protected by IP filtering to prevent unauthorized access.

### Configuration

Configure allowed IPs in `appsettings.json`:

```json
{
  "MetricsIpFilter": {
    "AllowedIps": [
      "127.0.0.1",
      "::1",
      "10.0.0.0/8",
      "172.16.0.0/12", 
      "192.168.0.0/16",
      "YOUR_PROMETHEUS_SERVER_IP"
    ]
  }
}
```

### Supported IP Formats

- **Single IPs**: `192.168.1.100`, `127.0.0.1`
- **CIDR ranges**: `10.0.0.0/8`, `192.168.0.0/16`
- **IPv6**: `::1`, `2001:db8::/32`

### Traefik Integration

Since you're using Traefik, the middleware properly handles:
- `X-Forwarded-For` header (uses first IP in chain)
- `X-Real-IP` header
- Direct connection IP as fallback

### Default Configuration

**Production** (`appsettings.json`):
- Localhost (`127.0.0.1`, `::1`)
- Private networks (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`)

**Development** (`appsettings.Development.json`):
- All IPs allowed (`0.0.0.0/0`) for testing

### Access Denied Response

Unauthorized IPs receive:
- HTTP 403 Forbidden
- Response body: "Access denied"
- Logged as warning with client IP

## Metrics Endpoint

The metrics are available at: `http://your-dai-lete-instance/metrics` (IP restricted)

Example output:
```
# HELP podcast_time_saved_seconds_total Total time saved by removing ads from podcasts
# TYPE podcast_time_saved_seconds_total counter
podcast_time_saved_seconds_total{podcast_id="abc-123-def",podcast_name="My Favorite Podcast",episode_id="ep-456"} 180.5
podcast_time_saved_seconds_total{podcast_id="xyz-789-ghi",podcast_name="Another Podcast",episode_id="ep-789"} 95.2

# HELP podcast_time_saved_seconds Time saved per episode by removing ads
# TYPE podcast_time_saved_seconds histogram
podcast_time_saved_seconds_bucket{podcast_id="abc-123-def",podcast_name="My Favorite Podcast",episode_id="ep-456",le="30"} 0
podcast_time_saved_seconds_bucket{podcast_id="abc-123-def",podcast_name="My Favorite Podcast",episode_id="ep-456",le="60"} 0
podcast_time_saved_seconds_bucket{podcast_id="abc-123-def",podcast_name="My Favorite Podcast",episode_id="ep-456",le="120"} 0
podcast_time_saved_seconds_bucket{podcast_id="abc-123-def",podcast_name="My Favorite Podcast",episode_id="ep-456",le="300"} 1
podcast_time_saved_seconds_bucket{podcast_id="abc-123-def",podcast_name="My Favorite Podcast",episode_id="ep-456",le="+Inf"} 1
```

## Implementation Details

- **Duration Detection**: Uses `ffprobe` to get audio file durations
- **Metrics Collection**: Triggered during episode processing in `PodcastServices.ProcessDownloadedEpisodeAsync()`
- **Error Handling**: Negative time saved values are logged as warnings and not recorded
- **Podcast Names**: Automatically extracted from RSS feed titles when available