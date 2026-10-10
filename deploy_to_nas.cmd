ssh 192.168.0.203  -l root  -i C:\Users\Oliver\.ssh\nas-id_ed25519    "./update_backupserverdaemon.sh"

choice /T 10 /C rs /D s /N /M "Schliessen mit S (schliesst sich selbst nach 10 Sekunden)" 
