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
- Dotnet 8
- FFMPEG
- socks5 proxy in another region

## Instructions
Set up a systemd service with the following env variables
- proxyAddress - your Socks5 proxy
- baseAddress - The url you can use to access the mp3 podcast files
- podcastStoragePath - (optional) directory where processed podcast files are stored (defaults to ./Podcasts)
- AUTH_USERNAME - username for web interface login (defaults to "admin")
- AUTH_PASSWORD - password for web interface login (defaults to "password")
