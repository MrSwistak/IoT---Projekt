# Projekt IoT - Azure

Dokumentacja w pliku dokumentacja_IoT.pdf

Repozytorium składa się z:
- plik Program.cs - metoda startowa main, która ładuje konfigurację lineConfiguration.json, mapuje na klasę LineSettings, tworzy npwy obiekt naszej klasy Line, metoda Up() i Wait(), by konsola się nie zamykała
- plik Line.cs - główny plik klasy głównej
- plik lineconfiguration.json - konfiguracja według wymagań projektu, pozwalająca uruchamiać inną instancję zmieniając connectionStringa, maksymalna liczba urządzeń, serwer testowy aplikacji
- plik lineSettings.cs - klasy do zmapowania na podstawie pliku lineCongiguration.json
- plik Machine.cs - klasa machines, na podstawie Case Study
- folder Enums z dwoma plikami: DeviceErrorEnum.cs i ProductionStatusEnum.cs 

Dodatkowo w repezytorium znajduje się:
- folder Analytics_Krzysztof_Wymysłowski - export z Azure Stream Analytics 
