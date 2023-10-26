export GHCR_USER=<USER>
export GHCR_PAT=<PERSONAL ACCESS TOKEN>
echo $GHCR_PAT | docker login ghcr.io -u $GHCR_USER --password-stdin

cd backupserverdaemon
docker-compose down
docker-compose pull
docker-compose up -d
docker-compose ps
cd ..
