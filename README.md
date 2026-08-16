# <img src="https://dai-lete.locksley.dev/icons/favicon.svg" width="48">  Dai-Lete


![There are no ads in Ba Sing Se](https://i.imgur.com/CNiWSXX.jpg)

Ever get annoyed at **D**ynamic **A**d **I**nsertion in podcasts? 

This program uses the fact that the same ad will likely never be run across the globe to remove try and remove the ads, the workflow looks like this:

1. Download a copy of the podcast both locally and via a proxy in another region.
2. Process the files via ffmpeg to uncompressed wav files.
3. Identify the portions of the podcast that are only in both files.
4. Generate a new mp3 containing only the segmnts in both files.
5. Replace the link in the podcast feed dynamically to point to your new file whilst keeping the rest of the feed identical.  

## Populated Dashboard Example
![Dashboard screenshot](https://i.imgur.com/Kiqg4hL.png)


## Requirements 
- Dotnet 10
- FFMPEG
- socks5 proxy in another region
- Valkey-compatible server for feed cache and episode job queue
- Shared podcast storage if running more than one frontend/worker node

## Instructions
Set up the app with the following environment variables:

- `proxyAddress` - your Socks5 proxy
- `baseAddress` - the URL clients use to access feeds and MP3 podcast files
- `podcastStoragePath` - optional directory where processed podcast files are stored, defaults to `./Podcasts`
- `AUTH_USERNAME` - username for web interface login, defaults to `admin`
- `AUTH_PASSWORD` - password for web interface login, defaults to `password`
- `Valkey__ConnectionString` - Valkey connection string, for example `valkey:6379`
- `Worker__Enabled` - set to `true` on the worker node and `false` on frontend nodes
- `Valkey__ConsumerName` - optional unique worker name, commonly the pod hostname

Example single-node settings:

```text
Worker__Enabled=true
Valkey__ConnectionString=localhost:6379
```

Example frontend node settings:

```text
Worker__Enabled=false
Valkey__ConnectionString=valkey:6379
```

Example worker node settings:

```text
Worker__Enabled=true
Valkey__ConnectionString=valkey:6379
Valkey__ConsumerName=dai-lete-worker-0
```

### High Availability

For multiple frontend nodes, mount the same podcast storage path on every frontend and worker node. Frontends serve the processed MP3 files from this shared path, while the worker writes processed episodes there.

Run only one worker `Worker__Enabled=false` so they only serve the web UI/API and enqueue jobs.
