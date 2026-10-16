# DTR i Instrukcja Obsługi: Samodzielny Symulator Unitronics Vision PLC/HMI v0.0.4 [virtualplc.exe]

## 1. Wstęp i Architektura Rozwiązania (Wersja 0.0.4)

Aplikacja **`virtualplc.exe`** (wersja **0.0.4**) to autonomiczny, natywny emulator sprzętowy rodziny sterowników **Unitronics Vision PLC/HMI** (w tym m.in. V130, V350, V430, V570, V1040, V1210) wraz z obsługą modułów Snap-In oraz przemysłowej magistrali rozszerzeń wejść/wyjść **EX-A2X** (do 8 modułów rozszerzeń).

Aplikacja powstała w 100% jako samodzielny, pojedynczy plik wykonywalny Windows (.NET 4.0 x86), niewymagający instalacji żadnych dodatkowych pakietów, serwerów bazodanowych, środowisk przeglądarkowych ani bibliotek zewnętrznych.

### Kluczowa zasada działania: Czysty Sprzęt (Pure Hardware Emulation)
- Sterownik po uruchomieniu **nie posiada zakodowanej żadnej sztywnej logiki komory suszarniczej**.
- W stanie początkowym sterownik znajduje się w fabrycznym trybie **OPLC Standby / STOP**:
  - Wszystkie wyjścia cyfrowe (DO) są fizycznie odłączone (stan niski / OFF).
  - Wyjścia analogowe (AO) podają napięcie 0.00 V.
  - Ekran graficzny wyświetla fabryczny ekran bootowania Unitronics z parametrami wybranego modelu OPLC oraz statusem `BRAK PROGRAMU UŻYTKOWNIKA`.
  - Silnik drabinkowy PLC oczekuje na wgranie programu z oprogramowania VisiLogic.
- Wykonywanie jakiejkolwiek logiki sterowania, liczenie czasów, regulacja sekcji czy sterowanie napędami falowników następuje **wyłącznie po załadowaniu rzeczywistego programu użytkownika** (z pliku `.vlp` / `.vlx` / `.udb` lub przez sieć PCOM TCP).
- W każdej chwili użytkownik może przywrócić sterownik do czystego stanu fabrycznego za pomocą przycisku **"Czysty OPLC"**.

---

## 2. Panel Konfiguracji Sprzętowej (Hardware Configuration Wizard)

Począwszy od wersji **v0.0.4**, przy uruchomieniu aplikacji jako pierwsze pojawia się dedykowane okno **Panelu Konfiguracji Sprzętowej**:

### 2.1. Funkcje Panelu Konfiguracji:
1. **Wybór Rodziny i Modelu Sterownika Vision:**
   - **Vision 350:** V350-35-RA22 (domyślny), V350-35-TRA22, V350-35-TA24, V350-35-R34, V350-35-T38.
   - **Vision 130:** V130-33-RA22, V130-33-TA24 (ekran graficzny STN 2.4", obsługa klawiatury).
   - **Vision 430:** V430-J-RA22, V430-J-TA24, V430-J-RH2 (panoramiczny kolorowy ekran 4.3" WQVGA 480x272 px).
   - **Vision 570:** V570-57-T20B (modułowy PLC z ekranem 5.7" QVGA 320x240 px, gniazdo Snap-In I/O).
   - **Vision 1040:** V1040-20-T20B (modułowy PLC z ekranem 10.4" SVGA 800x600 px, 9 klawiszy funkcyjnych F1..F9).
   - **Vision 1210:** V1210-T20BJ (modułowy PLC z ekranem 12.1" SVGA 800x600 px, płaska ramka frontowa).
2. **Moduły Snap-In (dla sterowników modułowych V570, V1040, V1210):**
   - V200-18-E1B, V200-18-E2B, V200-18-E3XB, V200-18-E4XB, V200-18-E5B, V200-18-E6B, V200-18-E62B, V200-18-ECB.
3. **Magistrala Rozszerzeń Wejść/Wyjść EX-A2X (Sloty 1..8):**
   - Możliwość włączenia/wyłączenia magistrali rozszerzeń adapterem EX-A2X.
   - Tabela konfiguracyjna 8 slotów z obsługą pełnej gamy modułów wejść/wyjść Unitronics:
     - `IO-DI8-RO8` (8 DI, 8 Wyjść przekaźnikowych - np. profil Komory nr 9),
     - `IO-DI8-TO8` (8 DI, 8 Wyjść tranzystorowych pnp),
     - `IO-DI16` (16 Wejść cyfrowych 24V),
     - `IO-TO16` (16 Wyjść tranzystorowych 24V),
     - `IO-RO8`, `IO-RO16` (8 lub 16 Wyjść przekaźnikowych),
     - `IO-AI4-AO2` (4 Wejścia analogowe 12-bit, 2 Wyjścia analogowe),
     - `IO-PT400`, `IO-PT4K` (4 Wejścia czujników temperatury PT100/PT1000),
     - `IO-AO6X` (6 Wyjść analogowych izolowanych 12-bit),
     - `EX-D16A3-RO8`, `EX-D16A3-TO16` (16 DI, 3 AI, 8/16 DO).
4. **Karta parametrów i podsumowanie I/O:**
   - Wyświetla na żywo rozdzielczość ekranu docelowego modelu, obecność ekranu dotykowego, strukturę pamięci (MB 4096, MI 2048, ML 512, DW 512, MF 64, T 256, C 32) oraz dokładną sumę kanałów DI, DO, AI i AO z przypisanymi zakresami adresowymi.
5. **Presety:**
   - `Komora 9 (V350 + IO-DI8-RO8)` – natychmiastowe ustawienie konfiguracji sprzętowej dla komory suszarniczej nr 9.
   - `Domyślny V350` – fabryczny profil V350-35-RA22 bez modułów rozszerzeń.
6. **Wybór Języka:**
   - Bezpośrednio w prawym górnym rogu panelu dostępny jest przełącznik języka interfejsu (Polski / English).
7. **Kontrola Startu:**
   - Opcja `Pokaż ten panel przy każdym starcie aplikacji` (możliwość wyłączenia, aby symulator uruchamiał się bezpośrednio z zapisanym profilem).
   - W każdej chwili konfigurację można otworzyć i zmienić z menu głównego: `Sprzęt -> Konfiguracja sprzętowa sterownika i I/O...`.

---

## 3. Dwujęzyczny Interfejs (Bilingual UI: Polski & English)

Symulator posiada wbudowany, kompletny system lokalizacji:
- **Język Polski (PL)** – pełne nazewnictwo techniczne, etykiety szafki I/O, komunikaty diagnostyczne.
- **Język Angielski (EN)** – międzynarodowe nazewnictwo automatyki przemysłowej.
- **Przełączanie języka:**
  - W panelu konfiguracji sprzętowej (rozwijana lista w prawym górnym rogu).
  - W menu głównym symulatora: `Ustawienia -> Język / Language -> Polski / English`.
  - W oknie `Ustawienia symulatora...` (zakładka Ogólne).
- Przełączenie następuje **w locie bez konieczności restartu symulatora** – wszystkie etykiety menu, paska narzędzi, nagłówki tabel, drzewo podprogramów i opisy natychmiast adaptują się do wybranego języka.

---

## 4. Opcje Logowania i Szczegółowości (Logging & Diagnostics)

W menu `Ustawienia -> Ustawienia symulatora...` (oraz w podmenu `Ustawienia -> Logowanie`) dostępna jest pełna kontrola nad mechanizmem logowania do pliku `virtualplc_debug.log`:

1. **Włącznik / Wyłącznik Logowania (`Logging Enabled`):**
   - Pozwala na całkowite wyłączenie zapisu do pliku dyskowego w celu maksymalizacji wydajności lub włączenie go na czas debugowania.
2. **Poziomy Szczegółowości Logowania (`Log Verbosity`):**
   - `Error` – rejestruje wyłącznie błędy krytyczne, awarie połączeń i nieobsługiwane wyjątki.
   - `Warn` – rejestruje ostrzeżenia, błędy odczytu baz Jet 4.0 oraz błędy.
   - `Info` (domyślny) – standardowy rejestr startu/stopu PLC, wczytywania projektów, połączeń PCOM TCP i zmian konfiguracji.
   - `Debug` – szczegółowy zapis pracy silnika drabinkowego, ewaluacji szczebli i procedur pobierania VisiLogic.
   - `Trace` – pełny zapis bajt po bajcie (HEX) wszystkich przychodzących i wychodzących ramek PCOM oraz zdarzeń dotyku HMI.
3. **Filtry Specjalistyczne:**
   - `Loguj pakiety protokołu PCOM TCP` – niezależny przełącznik monitorowania ruchu sieciowego VisiLogic.
   - `Loguj zdarzenia ekranu dotykowego (Touch)` – rejestracja współrzędnych kliknięć i wciskania przycisków ekranowych.
4. **Narzędzia Obsługi Logu:**
   - Przycisk `Otwórz plik logu` – natychmiastowe otwarcie `virtualplc_debug.log` w domyślnym edytorze systemowym.
   - Przycisk `Wyczyść plik logu` – wyzerowanie pliku logu bez konieczności zamykania symulatora.
5. **Trwałość Konfiguracji:**
   - Wszystkie ustawienia (język, zachowanie przy starcie, port PCOM, parametry logowania oraz profil sprzętowy) są automatycznie zapisywane w pliku `virtualplc_config.json` w katalogu programu.

---

## 5. Metody Wgrywania i Uruchamiania Programów z VisiLogic

Symulator udostępnia 3 równorzędne metody dostarczenia programu drabinkowego i ekranów HMI:

### Metoda 1: Szybkie wgranie projektu Komory nr 9 z Pulpitu
W pasku narzędziowym kliknij przycisk:
`[ ⚡ Komora_9_PLC.vlp ]`
Aplikacja natychmiast odnajdzie plik projektu zlokalizowany w katalogu użytkownika:
`C:\Users\Panasonic\Desktop\NARZĘDZIA\Visilogic\Projekty aktualne\Komora nr. 9 Gardno\Komora_9_PLC.vlp`
Rozpakuje bazę OPLC, wczyta szczeble drabinki, opisy zmiennych oraz ekrany HMI, a następnie automatycznie przełączy sterownik w tryb **RUN**.

### Metoda 2: Wgranie dowolnego pliku projektu VisiLogic (.vlp / .vlx / .udb)
W pasku narzędziowym kliknij przycisk:
`[ 📁 WGRAJ PROJEKT (.VLP) ]` lub z menu: `Plik -> Wgraj projekt VisiLogic...`
W oknie dialogowym wskaż dowolny plik projektu VisiLogic. Parser odczyta strukturę bazy danych Jet 4.0 zabezpieczoną fabrycznym kluczem producenta (`UniOpenSesame`).

### Metoda 3: Bezpośrednie Wgrywanie i Monitorowanie Online z VisiLogic (Download przez Port TCP 20256)
Symulator posiada zaawansowany serwer protokołu **Unitronics PCOM TCP** (ASCII i Binary) nasłuchujący na standardowym porcie `20256`:
1. Uruchom oprogramowanie **Unitronics VisiLogic**.
2. W menu VisiLogic wybierz: `Connection -> Communication & OS`.
3. Wybierz protokół `TCP/IP (Call)`.
4. Wpisz adres IP: `127.0.0.1` (localhost) oraz Port: `20256`.
5. Kliknij `Get OPLC Information` – VisiLogic zidentyfikuje sterownik zgodnie ze skonfigurowanym profilem sprzętowym (np. `V350-35-RA22`, `V570-57-T20B`, itp.) z wersją OS 4.04.
6. **Wgrywanie programu (Download):**
   - W VisiLogic wybierz `Connection -> Download -> Download` (skrót klawiszowy `F9`).
   - VisiLogic skompiluje projekt i wyśle zapytanie o status gotowości do pobrania (`GS` dla rejestru `SI 307`).
   - Symulator odpowiada prawidłową ramką PCOM ASCII i Binary.
   - Po zatwierdzeniu transferu przez VisiLogic, symulator automatycznie odczytuje ścieżkę do nowo skompilowanego projektu z bazy `OPLC.ini` i przeładowuje program w locie do wirtualnego PLC!

---

## 6. Elementy Interfejsu i Funkcjonalności

Ekran aplikacji podzielony jest na dwa główne obszary:

### 6.1. Lewy Panel: Fizyczny Pulpit Sterownika Vision
Odwzorowuje rzeczywisty panel czołowy wybranego sterownika:
- **Diody diagnostyczne CPU:**
  - `PWR` (Zielona) – sygnalizacja prawidłowego zasilania wewnętrznego 24V DC.
  - `RUN` (Zielona) – świeci ciągłym światłem w trybie RUN. Gaśnie w trybie STOP.
  - `ERR` (Czerwona) – sygnalizuje stan zatrzymania lub brak programu.
- **Ekran Kolorowy TFT:**
  - Automatycznie dopasowuje się do rozdzielczości wybranego modelu (320x240, 480x272, 800x600 px).
  - Wyświetla ekrany HMI zaprojektowane w VisiLogic.
  - **Pełna reakcja na dotyk (Touchscreen):** kliknięcie myszą w przycisk ekranowy natychmiast ustawia powiązany bit dotykowy.
  - **Dotykowa klawiatura numeryczna (Keypad):** kliknięcie w pole numeryczne ze zmienną nastawy otwiera wirtualną klawiaturę numeryczną.
- **Klawisze funkcyjne:**
  - Klawisze funkcyjne `[F1]` .. `[F5]` (lub `[F1]` .. `[F9]` dla V1040) sterują bitami systemowymi `SB 31` .. `SB 39`.

---

### 6.2. Prawy Panel: Zakładki Diagnostyczne i Symulacja Szafki I/O

#### Zakładka 1: Szafka I/O & Moduły Rozszerzeń EX-A2X
Wirtualna szafka obiektowa dynamicznie dopasowująca liczbę kontrolek do wybranego profilu sprzętowego:
- **Podzakładka Wejścia Cyfrowe (DI):**
  - Obsługa wszystkich wejść cyfrowych CPU, Snap-In oraz modułów magistrali EX-A2X (np. I0..I19 w profilu Komory 9).
  - Każde wejście posiada przełącznik bistabilny (hebelkowy), przycisk chwilowy (impuls) oraz diodę LED sygnalizującą stan wysoki na zacisku.
  - Opisy wejść są dynamicznie synchronizowane z etykietami załadowanego projektu VisiLogic.
- **Podzakładka Wyjścia Cyfrowe (DO):**
  - Wyjścia przekaźnikowe i tranzystorowe z żółtymi diodami LED stanu wysterowania cewek.
- **Podzakładka Wejścia Analogowe (AI):**
  - Precyzyjne suwaki oraz pola numeryczne do zadawania parametrów pomiarowych (0..10V, 4..20mA, PT100).
- **Podzakładka Wyjścia Analogowe (AO):**
  - Wskaźniki napięcia analogowego (0..10V) oraz przeliczonej częstotliwości zadawanej na falowniki (0..50.0 Hz).

#### Zakładka 2: Tabela Zmiennych (Live Watch Table)
- Podgląd w czasie rzeczywistym wszystkich rejestrów i bitów sterownika (`I`, `O`, `MB`, `MI`, `ML/DW`, `T`, `C`, `SB/SI`).
- Filtrowanie i wyszukiwanie po nazwie lub adresie operandu.
- **Wymuszanie wartości (Force Value):** podwójne kliknięcie na wierszu pozwala na natychmiastowe przełączenie stanu bitu lub zmianę wartości rejestru.

#### Zakładka 3: Drabinka PLC (Ladder Logic View)
- Drzewo podprogramów (`LdrFunctions`).
- Wizualizacja szczebli z podglądem styków NO/NC, zboczy, cewek, bloków matematycznych i timerów.

#### Zakładka 4: Nawigator Ekranów HMI
- Wykaz ekranów graficznych projektu z możliwością natychmiastowego przełączania podglądu (zapis do `SI 9`).

#### Zakładka 5: Dziennik Zdarzeń & PCOM oraz Log Debugowania
- Konsola zdarzeń systemowych, ramek PCOM oraz zmian stanów sterownika.
- Przyciski szybkiego otwierania i czyszczenia pliku logu `virtualplc_debug.log`.

---

## 7. Struktura Katalogów, Wersjonowanie i Dystrybucja

Pliki binarne symulatora (`virtualplc.exe`) są publikowane w oficjalnych wydaniach **[GitHub Releases](https://github.com/PROCHOTKILLER/Unitronics-Vision-PLC-HMI-Simulator/releases)**.

Struktura repozytorium:
1. **`SIMULATOR_MANUAL.md`** – dokumentacja techniczna i DTR.
2. **`README.md`** – opis projektu w języku angielskim.
3. **`Run_Simulator.bat`** – skrypt uruchomieniowy.
4. Podkatalog **`src/`** – kody źródłowe C# (.NET 4.0/4.8) oraz skrypty kompilacji.
5. Podkatalog **`archive/`** – automatyczne archiwum poprzednich wydań roboczych (maksymalnie 5 ostatnich wersji).

### Zawartość katalogu źródłowego (`src/`):
- `build.bat` – skrypt kompilacji i automatycznej rotacji wersji.
- `rotate_versions.ps1` – skrypt rotacji wydań w katalogu `archive/` (limit 5).
- `HardwareConfigDialog.cs` – modalne okno startowe kreatora konfiguracji sprzętowej i wyboru języka.
- `SettingsDialog.cs` – okno ustawień głównych symulatora (język, logowanie, sieć).
- `HardwareProfile.cs` – katalog modeli Vision, modułów Snap-In oraz modułów magistrali EX-A2X.
- `Localization.cs` – moduł dwujęzyczności (PL / EN) z dynamiczną zmianą w locie.
- `SimulatorConfig.cs` – obsługa konfiguracji i serializacja do `virtualplc_config.json`.
- `AppLogger.cs` – moduł bezpiecznego wielowątkowego logowania zdarzeń z poziomami szczegółowości.
- `Program.cs` – punkt startowy aplikacji WinForms wraz z procedurą uruchamiania kreatora sprzętu.
- `MainForm.cs` – zintegrowany pulpit operatorski.
- `PlcEngine.cs` – silnik programowy Soft-PLC z mapą pamięci i ewaluatorem drabinki.
- `VlpParser.cs` – parser projektów VisiLogic `.vlp` / `.udb`.
- `HmiRenderer.cs` – silnik renderowania ekranów graficznych TFT.
- `HardwarePanel.cs` – panel wirtualnej szafki I/O z dynamiczną przebudową kanałów.
- `PcomServer.cs` – serwer TCP protokołu Unitronics PCOM (:20256) z obsługą Downloadu.

### Zasada wersjonowania i archiwizacji (Katalog `archive/`):
- Numeracja wersji rozpoczyna się od **0.0.1** i jest inkrementowana przy każdej ingerencji w kod lub dokumentację (obecnie: **0.0.4**).
- Przed każdą nową kompilacją poprzednia wersja `virtualplc.exe` oraz instrukcji jest automatycznie przenoszona do dedykowanego podfolderu w `archive/vX.Y.Z/`.
- Katalog `archive/` przechowuje **dokładnie do 5 ostatnich wydań** – najstarsze wydania powyżej tego limitu są automatycznie usuwane podczas budowania nowej wersji.
- Oficjalne pliki binarne dla użytkowników są załączane bezpośrednio do wydań na GitHubie w sekcji **Releases**.

---

## 8. Historia Wersji

- **v0.0.4 (2026-10-17)**:
  - **Panel Konfiguracji Sprzętowej przed Uruchomieniem:** wprowadzenie okna powitalnego ze szczegółowym wyborem modelu sterownika z rodziny Vision (V130, V350, V430, V570, V1040, V1210), modułu Snap-In oraz 8 slotów magistrali rozszerzeń wejść/wyjść EX-A2X.
  - **Pełne przestrzeganie profilu sprzętowego:** symulator ściśle dopasowuje rozdzielczość ekranu TFT, obecność ekranu dotykowego, strukturę I/O szafki obiektowej oraz identyfikator zwracany do VisiLogic w ramce PCOM.
  - **Dwujęzyczny Interfejs (Bilingual UI):** dynamiczne przełączanie języka (Polski / English) w oknie konfiguracji startowej, menu głównym oraz ustawieniach symulatora.
  - **Zaawansowane Opcje Logowania w Ustawieniach:** włącznik/wyłącznik logowania, 5 poziomów szczegółowości (Error, Warn, Info, Debug, Trace), filtry pakietów PCOM i zdarzeń ekranu dotykowego, przyciski bezpośredniego podglądu i czyszczenia logu `virtualplc_debug.log`.
  - **Trwała Konfiguracja:** plik `virtualplc_config.json` zachowujący stan konfiguracji użytkownika pomiędzy uruchomieniami.
  - Automatyczna archiwizacja wersji v0.0.3 do katalogu `archive/v0.0.3/`.

- **v0.0.3 (2026-10-16)**:
  - **Pełna obsługa procedury Download z VisiLogic:** rozwiązanie problemu `5Automation error` z biblioteki `UnVsCon2.dll` poprzez kompletną implementację oficjalnego standardu protokołu Unitronics PCOM (PCOM ASCII i PCOM Binary).
  - Prawidłowa obsługa 6-bajtowego nagłówka PCOM TCP (Transaction ID, typ protokołu 101/102, długość danych LE).
  - Poprawny format odpowiedzi PCOM ASCII: prefiks `/A`, 2-znakowa suma kontrolna modulo 256 sumy bajtów oraz obsługa zapytań o status gotowości do pobrania `/00GS01330122` (rejestr systemowy SI 307).
  - Poprawny format ramek binarnych PCOM: magic `/_OPLC`, 24-bajtowy nagłówek, 16-bit complement CRC, obsługa komend zapisu bufora (`0x41`), zatwierdzenia flash (`0x1A`), translacji indeksów (`0x16`), autoryzacji hasła (`0x02`) oraz zapytania o nazwę (`0x0C`).
  - Automatyczna detekcja skompilowanego projektu z systemowej bazy danych `OPLC.ini` (`[3000] 3001=...`) i natychmiastowe dynamiczne przeładowanie programu drabinkowego, operandów oraz ekranów HMI do symulatora po wgraniu programu w VisiLogic.
  - Automatyczna archiwizacja wersji v0.0.2 do `archive/v0.0.2/`.

- **v0.0.2 (2026-10-16)**:
  - Wdrożenie funkcji logowania debugowania do pliku tekstowego `virtualplc_debug.log` w katalogu uruchomienia aplikacji (`AppDomain.CurrentDomain.BaseDirectory`).
  - Dodanie klasy `AppLogger` z bezpiecznym zapisem wielowątkowym, poziomami logowania (INFO, DEBUG, ERROR, PCOM, TOUCH) i rotacją pliku przy 15 MB.
  - Globalne przechwytywanie nieobsłużonych wyjątków (`ThreadException`, `UnhandledException`) z pełnym zrzutem śladu stosu (Stack Trace) do pliku tekstowego.
  - Dodanie przycisku szybkiego podglądu pliku logu w zakładce „Dziennik Zdarzeń & PCOM” oraz w menu `Pomoc`.
  - Automatyczna archiwizacja wersji v0.0.1 do katalogu `archive/v0.0.1/`.

- **v0.0.1 (2026-10-16)**:
  - Wprowadzenie oficjalnego wersjonowania semantycznego od numeru 0.0.1.
  - Reorganizacja struktury plików: odseparowanie katalogu roboczego (`src/`) od głównego repozytorium.
  - Wdrożenie automatycznej archiwizacji do katalogu `archive/` z zachowaniem 5 ostatnich wersji.
  - Pełne kodowanie UTF-8 (eliminacja efektu mojibake przy polskich znakach diakrytycznych).
  - Natychmiastowa reakcja ekranu TFT HMI na dotyk (odświeżanie poniżej 15 ms).
  - Pełne ładowanie bazy operandów MB 0..255 i MI 0..127 z projektów VisiLogic.
  - Odchudzenie panelu przedniego V350 – pozostawienie wyłącznie rzeczywistych klawiszy funkcyjnych F1..F5.
