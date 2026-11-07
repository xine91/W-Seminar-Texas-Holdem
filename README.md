--W-Seminar-Mathe:

PokerSim ist eine ASP.NET-Core-Razor-Pages-Webanwendung zur Simulation und Auswertung von Texas-Hold’em-Pokerhänden.

Wie startet man die Simulationen lokal?

--Voraussetzungen:

Das .NET 10 Framework muss installiert sein (https://dotnet.microsoft.com/download/dotnet/10.0).

Visual Studio installieren(https://visualstudio.microsoft.com/de/) und während der Installation die Workloads „Web Razor Pages“ und „.NET“ auswählen.

Das Repository über den Link ... herunterladen.

In Visual Studio das Repository öffnen und W-Seminar als „Startprojekt“ auswählen und die Simulation starten.

Anschließend `http://localhost:5185` im Browser öffnen. Zum Beenden im Terminal **Strg+C** drücken. Falls das Startprofil geändert wurde, gilt die beim Start angezeigte Adresse.


--Daten:

Simulationsergebnisse werden standardmäßig unter `W Seminar/Data` gespeichert. Das Verzeichnis muss für die Anwendung beschreibbar sein. Wenn noch keine Simulationsergebnisse vorhanden sind, werden beim Start im Hintergrund Beispieldaten erzeugt. Die Einstellungen dafür stehen in `W Seminar/appsettings.json` unter `PokerSim`.
