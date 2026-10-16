# DTR i Instrukcja Obsługi: Samodzielny Symulator Unitronics Vision PLC/HMI v0.0.3 [virtualplc.exe]

## 1. Wstęp i Architektura Rozwiązania (Wersja 0.0.3)

Aplikacja **`virtualplc.exe`** (wersja **0.0.3**) to autonomiczny, natywny emulator sprzętowy rodziny sterowników **Unitronics Vision PLC/HMI** (w tym m.in. V130, V350, V430, V570, V700, V1040, V1210 z domyślnym profilem V350-35-RA22) wraz z przemysłowym modułem rozszerzeń wejść/wyjść **EX-A2X**.

Aplikacja powstała w 100% jako samodzielny, pojedynczy plik wykonywalny Windows (.NET 4.0 x86), niewymagający instalacji żadnych dodatkowych pakietów, serwerów bazodanowych, środowisk przeglądarkowych ani bibliotek zewnętrznych.

### Kluczowa zasada działania: Czysty Sprzęt (Pure Hardware Emulation)
- Sterownik po uruchomieniu **nie posiada zakodowanej żadnej sztywnej logiki komory suszarniczej**.
- W stanie początkowym sterownik znajduje się w fabrycznym trybie **OPLC Standby / STOP**:
  - Wszystkie wyjścia cyfrowe (O0..O15) są fizycznie odłączone (stan niski / OFF).
  - Wyjścia analogowe (AO0..AO1) podają napięcie 0.00 V (0.0 Hz).
  - Kolorowy ekran TFT 3.5" (320x240 px) wyświetla fabryczny ekran bootowania Unitronics z parametrami OPLC oraz statusem `BRAK PROGRAMU UŻYTKOWNIKA`.
  - Silnik drabinkowy PLC oczekuje na wgranie programu z oprogramowania VisiLogic.
- Wykonywanie jakiejkolwiek logiki sterowania, liczenie czasów, regulacja sekcji czy sterowanie napędami falowników następuje **wyłącznie po załadowaniu rzeczywistego programu użytkownika** (z pliku `.vlp` / `.vlx` / `.udb` lub przez sieć PCOM TCP).
- W każdej chwili użytkownik może przywrócić sterownik do czystego stanu fabrycznego za pomocą przycisku **"Czysty OPLC"**.

---

## 2. Metody Wgrywania i Uruchamiania Programów z VisiLogic

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
5. Kliknij `Get OPLC Information` – VisiLogic natychmiast zidentyfikuje sterownik jako model `V350-35-RA22` z wersją OS 4.04.
6. **Wgrywanie programu (Download):**
   - W VisiLogic wybierz `Connection -> Download -> Download` (skrót klawiszowy `F9`).
   - VisiLogic skompiluje projekt i wyśle zapytanie o status gotowości do pobrania (`GS` dla rejestru `SI 307`).
   - Symulator v0.0.3 w pełni odpowiada prawidłową ramką PCOM ASCII i Binary z sumami kontrolnymi 2's complement.
   - Po zatwierdzeniu transferu przez VisiLogic, symulator automatycznie odczytuje ścieżkę do nowo skompilowanego projektu z systemowej bazy `OPLC.ini` i przeładowuje program w locie do wirtualnego PLC!

---

## 3. Elementy Interfejsu i Funkcjonalności

Ekran aplikacji podzielony jest na dwa główne obszary:

### 3.1. Lewy Panel: Fizyczny Pulpit Sterownika Unitronics V350
Odwzorowuje rzeczywisty panel czołowy zamontowany na drzwiach szafy sterowniczej:
- **Diody diagnostyczne CPU:**
  - `PWR` (Zielona) – sygnalizacja prawidłowego zasilania wewnętrznego 24V DC.
  - `RUN` (Zielona) – świeci ciągłym światłem, gdy sterownik wykonuje cykl skanowania drabinki (tryb RUN). Gaśnie w trybie STOP.
  - `ERR` (Czerwona) – sygnalizuje stan zatrzymania lub brak programu.
- **Ekran Kolorowy TFT 3.5" (320x240 pikseli):**
  - Wyświetla ekrany HMI zaprojektowane w VisiLogic (Startup, Kontrola Procesu, Nastawy, Wykresy, Testy, Serwis).
  - **Pełna, natychmiastowa reakcja na dotyk (Touchscreen):** kliknięcie myszą w przycisk ekranowy natychmiast ustawia powiązany bit dotykowy (np. bity nawigacji `MB 210..MB 220` zmieniające `SI 9`, bity chwilowe `MB 20..24` START/STOP/PAUZA lub bity toggle testów `MB 40`, `MB 90..104`).
  - **Wbudowana dotykowa klawiatura numeryczna (Keypad):** kliknięcie w pole numeryczne ze zmienną nastawy (zaznaczone w VisiLogic z opcją Keypad Entry) otwiera na ekranie wirtualną klawiaturę numeryczną, umożliwiając wpisanie nowej nastawy za pomocą przycisków 0..9, +/-, CLR, OK.
- **Klawiatura Membranowa V350:**
  - Zgodnie z fizyczną konstrukcją panelu Vision350, pod ekranem znajduje się wyłącznie 5 klawiszy funkcyjnych:
  - Klawisze funkcyjne `[F1]` .. `[F5]` – wciśnięcie generuje impuls na bitach systemowych `SB 31` .. `SB 35` sterownika PLC.

---

### 3.2. Prawy Panel: Zakładki Diagnostyczne i Symulacja Szafki I/O

#### Zakładka 1: Szafka I/O & Moduł EX-A2X
Wirtualna szafka obiektowa z fizycznymi elementami łączeniowymi:
- **Wejścia Cyfrowe DI (I0 .. I19):**
  - Wejścia CPU `I0..I11` oraz modułu rozszerzeń `I12..I19`.
  - Każde wejście posiada przełącznik bistabilny (hebelkowy), przycisk chwilowy (impuls) oraz diodę LED sygnalizującą stan wysoki na zacisku.
  - Opisy wejść są dynamicznie synchronizowane z etykietami załadowanego projektu VisiLogic (np. `I0: Termik F1 Wentylatory`, `I1: Termik F2 Grzałki`, `I2: Przycisk Start`, itd.).
- **Wyjścia Cyfrowe DO (O0 .. O15):**
  - Wyjścia przekaźnikowe i tranzystorowe CPU `O0..O7` oraz modułu `O8..O15`.
  - Diody LED (żółte) sygnalizują wysterowanie cewek styczników wentylatorów, grzałek, elektrozaworów zraszania, siłowników klap i sygnalizatora alarmowego.
- **Wejścia Analogowe AI (AI0 .. AI3):**
  - Precyzyjne suwaki oraz pola numeryczne do zadawania parametrów:
    - `AI0`: Pomiar ogólny 0..1023 (0..10V).
    - `AI1`: Wilgotność względna komory RH 0..1023 (4..20mA).
    - `AI2`: Temperatura powietrza w komorze Pt100 (0.0 .. 150.0 °C).
    - `AI3`: Temperatura rdzenia drewna Pt100 (0.0 .. 150.0 °C).
- **Wyjścia Analogowe AO (AO0, AO1):**
  - Wskaźniki napięcia analogowego (0..10V) oraz przeliczonej częstotliwości zadawanej na falowniki wentylatorów rewersyjnych (0..50.0 Hz).

#### Zakładka 2: Tabela Zmiennych (Live Watch Table)
- Tabela podglądu w czasie rzeczywistym wszystkich rejestrów i bitów sterownika.
- Pełna baza bitów pamięci `MB 0..255` oraz rejestrów `MI 0..127` wraz z ich opisami z bazy VisiLogic.
- Szybkie, niezawodne filtrowanie wg typu operandu: Wszystkie, `I`, `O`, `MB`, `MI`, `ML/DW`, `T`, `SB/SI`.
- Wyszukiwarka tekstowa po nazwie lub adresie (np. `Wentylator`, `Temperatura`, `MB 22`, `MI 10`).
- **Funkcja Wymuszania (Force Value):** podwójne kliknięcie na dowolnym wierszu pozwala na natychmiastowe przełączenie stanu bitu (TRUE/FALSE) lub wpisanie nowej wartości rejestru.

#### Zakładka 3: Drabinka PLC (Ladder Logic View)
- Drzewo podprogramów (`LdrFunctions`): `Main Routine`, `01_Awaryjne`, `02_Wentylatory`, `03_Nagrzewnica`, `04_Nawilzanie`, `05_Klapy`, `06_Pomiary`, itp.
- Wizualizacja szczebli z komentarzami technicznymi oraz podglądem styków NO/NC, zboczy rosnących/opadających, cewek Direct/Set/Reset/Toggle, bloków matematycznych i timerów wraz z ich bieżącym stanem logicznym.

#### Zakładka 4: Nawigator Ekranów HMI
- Wykaz wszystkich ekranów graficznych projektu wraz z ID, nazwą i liczbą kontrolek.
- Przełączenie ekranu następuje przez zaznaczenie i kliknięcie przycisku lub podwójne kliknięcie w pozycję na liście (natychmiastowy zapis do rejestru systemowego `SI 9` i odświeżenie ekranu TFT).

#### Zakładka 5: Dziennik Zdarzeń & PCOM oraz Log Debugowania
- Rejestr konsolowy wszystkich operacji systemowych, przejść stanów RUN/STOP, ramek komunikacyjnych PCOM oraz operacji ręcznego wymuszania zmiennych.
- **Plik logu debugera (`virtualplc_debug.log`)**:
  - Aplikacja automatycznie i na bieżąco rejestruje pełny dziennik działania w pliku tekstowym `virtualplc_debug.log`, zlokalizowanym bezpośrednio w katalogu skąd aplikacja została uruchomiona (`AppDomain.CurrentDomain.BaseDirectory`).
  - Log zawiera precyzyjne znaczniki czasu (z dokładnością do milisekund), identyfikatory wątków, poziomy zdarzeń (`[INFO]`, `[DEBUG]`, `[WARN]`, `[ERROR]`, `[PCOM]`, `[TOUCH]`), pełne zrzuty błędów ze śladem stosu (Stack Trace) oraz rejestrację nieobsługiwanych wyjątków.
  - Szybki dostęp: kliknij przycisk `[ 📄 Otwórz plik logu (debug.log) ]` w zakładce Dziennik lub wybierz z menu `Pomoc -> Otwórz plik logu debugera...`.
  - Mechanizm automatycznej ochrony: przy przekroczeniu 15 MB rozmiaru plik jest automatycznie rotowany do `.old.log`.

---

## 4. Struktura Katalogów, Wersjonowanie i Kompilacja

Katalog symulatora: `narzedzia/symulator v350 portable/`

Zgodnie ze standardem czystej dystrybucji, w katalogu głównym symulatora znajdują się **wyłącznie**:
1. **`virtualplc.exe`** – kompletny, samodzielny plik wykonywalny symulatora (wersja 0.0.3).
2. **`INSTRUKCJA_SYMULATORA.md`** – niniejsza dokumentacja i DTR (wersja 0.0.3).
3. Podkatalog **`roboczy/`** – środowisko deweloperskie i pliki robocze.
4. Podkatalog **`stare wersje/`** – automatyczne archiwum poprzednich wydań (maksymalnie 5 ostatnich wersji).

### Zawartość katalogu roboczego (`roboczy/`):
- `build.bat` – skrypt kompilacji i rotacji wersji.
- `rotate_versions.ps1` – skrypt automatycznej rotacji archiwalnych wydań (limit 5).
- `AppLogger.cs` – moduł bezpiecznego wielowątkowego logowania zdarzeń do pliku tekstowego.
- `Program.cs` – punkt startowy aplikacji WinForms wraz z atrybutami wersji zestawu i przechwytywaniem wyjątków.
- `MainForm.cs` – zintegrowany pulpit operatorski.
- `PlcEngine.cs` – silnik programowy Soft-PLC z mapą pamięci i ewaluatorem drabinki.
- `VlpParser.cs` – bezpośredni parser plików `.vlp` / `.udb` z odczytem tabeli `LdrOperands`.
- `HmiRenderer.cs` – silnik renderowania TFT 3.5" (320x240) z dotykiem i słupkami bargraph.
- `HardwarePanel.cs` – panel wirtualnej szafki I/O z przełącznikami i suwakami.
- `PcomServer.cs` – serwer TCP protokołu Unitronics PCOM (:20256) z obsługą PCOM ASCII & Binary oraz Download.
- `inspect_udb.ps1` – skrypt diagnostyczny bazy danych projektu.
- `Uruchom_Symulator_V350.bat` – pomocniczy skrypt uruchamiający.

### Zasada wersjonowania i archiwizacji (Katalog `stare wersje/`):
- Numeracja wersji rozpoczyna się od **0.0.1** i jest inkrementowana przy każdej ingerencji w kod lub dokumentację (obecnie: **0.0.3**).
- Przed każdą nową kompilacją poprzednia wersja `virtualplc.exe` oraz instrukcji jest automatycznie przenoszona do dedykowanego podfolderu w `stare wersje/vX.Y.Z/`.
- Katalog `stare wersje/` przechowuje **dokładnie do 5 ostatnich wydań** – najstarsze wydania powyżej tego limitu są automatycznie usuwane podczas budowania nowej wersji.

---

## 5. Historia Wersji

- **v0.0.3 (2026-10-16)**:
  - **Pełna obsługa procedury Download z VisiLogic:** rozwiązanie problemu `5Automation error` z biblioteki `UnVsCon2.dll` poprzez kompletną implementację oficjalnego standardu protokołu Unitronics PCOM (PCOM ASCII i PCOM Binary).
  - Prawidłowa obsługa 6-bajtowego nagłówka PCOM TCP (Transaction ID, typ protokołu 101/102, długość danych LE).
  - Poprawny format odpowiedzi PCOM ASCII: prefiks `/A`, 2-znakowa suma kontrolna modulo 256 sumy bajtów oraz obsługa zapytań o status gotowości do pobrania `/00GS01330122` (rejestr systemowy SI 307).
  - Poprawny format ramek binarnych PCOM: magic `/_OPLC`, 24-bajtowy nagłówek, 16-bit complement CRC, obsługa komend zapisu bufora (`0x41`), zatwierdzenia flash (`0x1A`), translacji indeksów (`0x16`), autoryzacji hasła (`0x02`) oraz zapytania o nazwę (`0x0C`).
  - Automatyczna detekcja skompilowanego projektu z systemowej bazy danych `OPLC.ini` (`[3000] 3001=...`) i natychmiastowe dynamiczne przeładowanie programu drabinkowego, operandów oraz ekranów HMI do symulatora po wgraniu programu w VisiLogic.
  - Automatyczna archiwizacja wersji v0.0.2 do `stare wersje/v0.0.2/`.

- **v0.0.2 (2026-10-16)**:
  - Wdrożenie funkcji logowania debugowania do pliku tekstowego `virtualplc_debug.log` w katalogu uruchomienia aplikacji (`AppDomain.CurrentDomain.BaseDirectory`).
  - Dodanie klasy `AppLogger` z bezpiecznym zapisem wielowątkowym, poziomami logowania (INFO, DEBUG, ERROR, PCOM, TOUCH) i rotacją pliku przy 15 MB.
  - Globalne przechwytywanie nieobsłużonych wyjątków (`ThreadException`, `UnhandledException`) z pełnym zrzutem śladu stosu (Stack Trace) do pliku tekstowego.
  - Dodanie przycisku szybkiego podglądu pliku logu w zakładce „Dziennik Zdarzeń & PCOM” oraz w menu `Pomoc`.
  - Automatyczna archiwizacja wersji v0.0.1 do katalogu `stare wersje/v0.0.1/`.

- **v0.0.1 (2026-10-16)**:
  - Wprowadzenie oficjalnego wersjonowania semantycznego od numeru 0.0.1.
  - Reorganizacja struktury plików: odseparowanie katalogu roboczego (`roboczy/`) od głównego folderu dystrybucyjnego.
  - Wdrożenie automatycznej archiwizacji do katalogu `stare wersje/` z zachowaniem 5 ostatnich wersji.
  - Pełne kodowanie UTF-8 (eliminacja efektu mojibake przy polskich znakach diakrytycznych).
  - Natychmiastowa reakcja ekranu TFT HMI na dotyk (odświeżanie poniżej 15 ms).
  - Pełne ładowanie bazy operandów MB 0..255 i MI 0..127 z projektów VisiLogic.
  - Odchudzenie panelu przedniego V350 – pozostawienie wyłącznie rzeczywistych klawiszy funkcyjnych F1..F5.

