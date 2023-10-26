call c:\Credentials\set_ghcr_credentials.cmd
echo %GHCR_PAT% | docker login ghcr.io -u %GHCR_USER% --password-stdin
