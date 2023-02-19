using Microsoft.Azure.Devices;
using Opc.UaFx;
using Opc.UaFx.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using System.Security.Cryptography;
using Opc.Ua;

namespace AzureFactoryIOT
{
    public class Line
    {
        private readonly LineSettings _lineSettings;
        private int _deviceIndex;

        public Line(LineSettings lineSettings)
        {
            _lineSettings = lineSettings;
            _deviceIndex = 0;
        }

        public async Task Up()
        {
            try
            {
                //Pobranie adresu serwera OPC UA z obiektu LineSettings
                var clientAddress = _lineSettings.OpcUAServer;

                //Utworzenie nowego klienta OPC z wykorzystaniem pobranego adresu
                using var client = new OpcClient(clientAddress);

                //Podłączenie klienta OPC do serwera
                client.Connect();

                //Pobranie identyfikatorów urządzeń z metody GetDevicesIds asynchronicznie
                var devicesIds = await GetDevicesIds();

                //Przegląd folderu obiektów serwera OPC i pobiera informacje dla określonych identyfikatorów urządzeń
                var machines = Browse(client.BrowseNode(OpcObjectTypes.ObjectsFolder), devicesIds, client);

                //Iteruj przez każdą maszynę i odczytaj wartości jej węzłów, serializuj stan maszyny i ustaw bliźniaki asynchronicznie
                foreach (var machine in machines)
                {
                    ReadMachineNodesValues(client, machine);
                    var options = new JsonSerializerOptions
                    {
                        IgnoreReadOnlyProperties = true
                    };
                    string machineState = JsonSerializer.Serialize(machine, options);

                    await SetTwinAsync(machine);

                    //Jeśli błąd urządzenia nie jest None, zaktualizuj bliźniaka asynchronicznie
                    if (machine.DeviceError != Enums.DeviceErrorEnum.None)
                    {
                        await UpdateTwinAsync(machine);
                    }
                }

                //Asynchroniczny odczyt danych OPC z serwera dla wszystkich maszyn
                await ReadOpcData(client, machines);
            }
            catch (Exception ex)
            {
                //Jeśli wystąpi wyjątek, wypisz komunikat na konsolę
                Console.WriteLine(ex.Message);
            }
        }


        private async Task<List<string>> GetDevicesIds()
        {
            // Tworzenie pustej listy identyfikatorów urządzeń
            var ids = new List<string>();

            // Sprawdź, czy łańcuchy połączeń i nazwa urządzenia nie mają wartości null
            if (_lineSettings.ConnectionStrings == null || _lineSettings.DeviceNameRegex == null)
            {
                return ids;
            }


            // Pobieranie urządzeń jako bliźniaków z IoT Hub przy użyciu łańcucha połączenia właściciela i maksymalnej liczby urządzeń określonej w obiekcie LineSettings
            var devices = RegistryManager.CreateFromConnectionString(_lineSettings.ConnectionStrings["owner"]).CreateQuery("select * from devices", _lineSettings.DeviceMaxCount);

            //Podczas gdy jest więcej wyników do pobrania z zapytania, iterujemy przez każdą stronę wyników i dodajemy do listy identyfikatory urządzeń, które pasują do regexa nazwy urządzenia
            while (devices.HasMoreResults)
            {
                var page = await devices.GetNextAsTwinAsync();
                foreach (var twin in page)
                {
                    if (Regex.IsMatch(twin.DeviceId, _lineSettings.DeviceNameRegex))
                    {
                        ids.Add(twin.DeviceId);
                    }
                }
            }

            //Zwróć listę identyfikatorów urządzeń
            return ids;
        }

        private List<Machine> Browse(OpcNodeInfo node, List<string> deviceIds, OpcClient client, int level = 0)
        {
            // Tworzenie pustej listy maszyn
            var nodes = new List<Machine>();

            // Sprawdź, czy identyfikator węzła maszyny regex i łańcuchy połączeń nie mają wartości null
            if (_lineSettings.MachineNodeIdRegex == null || _lineSettings.ConnectionStrings == null)
            {
                // Jeśli żaden z warunków nie jest spełniony, zwróć pustą listę maszyn
                return nodes;
            }

            // Sprawdź, czy identyfikator bieżącego węzła pasuje do identyfikatora węzła maszyny regex
            if (Regex.IsMatch(node.NodeId.ValueAsString, _lineSettings.MachineNodeIdRegex))
            {
                // Jeśli ID węzła bieżącego węzła pasuje do regexu ID węzła maszyny, utwórz klienta urządzenia z łańcucha połączenia określonego w obiekcie LineSettings, używając ID urządzenia w bieżącym indeksie urządzenia
                DeviceClient deviceClient = DeviceClient.CreateFromConnectionString(_lineSettings.ConnectionStrings["device"], deviceIds[_deviceIndex]);

                // Utworzenie nowego obiektu maszyny z aktualnego węzła, klienta urządzenia, klienta OPC i ID urządzenia przy aktualnym indeksie urządzenia
                var machine = new Machine(node.NodeId.ToString(), deviceClient, client, deviceIds[_deviceIndex]);

                // Ustawienie metod bezpośrednich dla wywołań metod klienta urządzenia
                machine.DeviceClient.SetMethodHandlerAsync("EmergencyStop", HandleEmergencyStop, machine);
                machine.DeviceClient.SetMethodHandlerAsync("LowKpiDetected", HandleLowKPI, machine);
                machine.DeviceClient.SetMethodHandlerAsync("ResetErrors", ResetErrors, machine);
                machine.DeviceClient.SetMethodHandlerAsync("MaintenanceDate", MaintenanceDate, machine);

                // Dodaj obiekt maszyny do listy maszyn
                nodes.Add(machine);

                // Zwiększanie indeksu urządzenia
                _deviceIndex++;
            }

            // Zwiększanie licznika poziomów
            level++;

            // Rekursywnie iterujemy przez każdy węzeł potomny bieżącego węzła i dodajemy wszystkie maszyny do listy maszyn
            foreach (var childNode in node.Children())
            {
                nodes.AddRange(Browse(childNode, deviceIds, client, level));
            }

            // Zwraca listę maszyn
            return nodes;
        }

        private async Task ReadOpcData(OpcClient client, IEnumerable<Machine> machines)
        {
            // Główna pętla odczytuje dane z OPC i przetwarza je dla każdej maszyny w kolekcji
            while (true)
            {
                foreach (var machine in machines)
                {
                    // Odczytaj wartości z OPC dla maszyny
                    ReadMachineNodesValues(client, machine);

                    // Jeśli produkcja jest zatrzymana, przejdź do kolejnej maszyny
                    if (machine.ProductionStatus == Enums.ProductionStatusEnum.Stopped) continue;

                    // Serializuj obiekt maszyny do formatu JSON i wyślij jako wiadomość do chmury
                    var options = new JsonSerializerOptions
                    {
                        IgnoreReadOnlyProperties = true
                    };
                    string machineState = JsonSerializer.Serialize(machine, options);

                    Console.WriteLine("Sendind Device-To-Cloud message");

                    await SendMessage(machine.DeviceClient, machineState);

                    // Jeśli wystąpił błąd, zaktualizuj właściwości raportowane w twinie urządzenia
                    if (machine.DeviceError != Enums.DeviceErrorEnum.None)
                    {
                        await UpdateTwinAsync(machine);
                    }
                }

                // Poczekaj przez określony czas przed kolejnym odczytem wartości z OPC
                await Task.Delay(_lineSettings.ReadingValuesDelay);
            }
        }

        private void ReadMachineNodesValues(OpcClient client, Machine machine)
        {
            // Odczytaj wartość węzła statusu produkcji z klienta OPC i przypisz ją do właściwości ProductionStatus obiektu Machine.
            machine.ProductionStatus = (Enums.ProductionStatusEnum)client.ReadNode(machine.ProductrionStatusNode).Value;

            // Odczytaj wartość węzła identyfikatora zamówienia produkcyjnego z klienta OPC i sparsuj ją jako GUID, następnie przypisz ją do właściwości WorkOrderId obiektu Machine.
            if (Guid.TryParse(client.ReadNode(machine.WorkOrderIdNode).Value.ToString(), out Guid result))
            {
                machine.WorkOrderId = result;
            }
            // Jeśli wartość nie może zostać sparsowana jako GUID, przypisz nowy GUID do właściwości WorkOrderId obiektu Machine.
            else
            {
                machine.WorkOrderId = new Guid();
            }
            // Odczytaj wartość węzła tempa produkcji z klienta OPC i przypisz ją do właściwości Rate obiektu Machine.
            machine.Rate = (int)client.ReadNode(machine.RateNode).Value;

            // Odczytaj wartość węzła liczby dobrych wyrobów z klienta OPC i przypisz ją do właściwości CountGood obiektu Machine.
            machine.CountGood = (long)client.ReadNode(machine.CountGoodNode).Value;

            // Odczytaj wartość węzła liczby wadliwych wyrobów z klienta OPC i przypisz ją do właściwości CountBad obiektu Machine.
            machine.CountBad = (long)client.ReadNode(machine.CountBadNode).Value;

            // Odczytaj wartość węzła temperatury z klienta OPC i przypisz ją do właściwości Temperature obiektu Machine.
            machine.Temperature = (double)client.ReadNode(machine.TemperatureNode).Value;

            // Odczytaj wartość węzła błędu urządzenia z klienta OPC i przypisz ją do właściwości DeviceError obiektu Machine.
            machine.DeviceError = (Enums.DeviceErrorEnum)client.ReadNode(machine.ErrorNode).Value;
        }

        private async Task SendMessage(DeviceClient client, string message)
        {
            // Utwórz nową wiadomość Azure IoT Hub na podstawie podanego ciągu znaków i ustaw jej typ i kodowanie zawartości.
            Microsoft.Azure.Devices.Client.Message deviceMessage = new Microsoft.Azure.Devices.Client.Message(Encoding.UTF8.GetBytes(message))
            {
                ContentType = "application/json",
                ContentEncoding = "UTF-8"

            };
            // Wyślij wiadomość do Azure IoT Hub przy użyciu podanego obiektu DeviceClient.
            await client.SendEventAsync(deviceMessage);
        }

        #region C2D HANDLERS


        // Metoda poniżej obsługuje zdalne wywołanie w przypadku zdarzenia związanego z zatrzymaniem awaryjnym
        private static async Task<MethodResponse> HandleEmergencyStop(MethodRequest request, object userContext)
        {
            var machine = (Machine)userContext;
            Console.WriteLine(request.Name);
            Console.WriteLine(new string('-', 20));
            Console.WriteLine($"Emergency stop received for {machine.DeviceId}");
            Console.WriteLine(new string('-', 20));

            machine.OpcClient.CallMethod(machine.Id, machine.EmergencyStopNode);

            return new MethodResponse(0);
        }


        // Metoda poniżej obsługuje zdalne wywołanie w przypadku wykrycia niskiego współczynnika wydajności
        private static async Task<MethodResponse> HandleLowKPI(MethodRequest request, object userContext)
        {
            var machine = (Machine)userContext;
            Console.WriteLine(request.Name);
            Console.WriteLine(new string('-', 20));
            Console.WriteLine($"Low KPI detected for {machine.DeviceId}");
            Console.WriteLine(new string('-', 20));

            machine.OpcClient.WriteNode(machine.RateNode, machine.Rate - 10);

            return new MethodResponse(0);
        }


        //Metoda ResetErrors resetuje wszystkie błędy dla urządzenia poprzez wywołanie metody OPC UA. 
        private static async Task<MethodResponse> ResetErrors(MethodRequest request, object userContext)
        {
            var machine = (Machine)userContext;
            Console.WriteLine(request.Name);
            Console.WriteLine(new string('-', 20));
            Console.WriteLine($"Reset all errors received for {machine.DeviceId}");
            Console.WriteLine(new string('-', 20));

            machine.OpcClient.CallMethod(machine.Id, machine.ResetErrorsNode);

            return new MethodResponse(0);
        }


        //Metoda MaintenanceDate aktualizuje zgłoszone właściwości bliźniaka urządzenia o bieżącą datę i godzinę ostatniej kontroli konserwacyjnej.
        private static async Task<MethodResponse> MaintenanceDate(MethodRequest methodRequest, object userContext)
        {
            var reportedProperties = new TwinCollection();
            reportedProperties["LastMaintenanceDate"] = DateTime.Now;

            await ((Machine)userContext).DeviceClient.UpdateReportedPropertiesAsync(reportedProperties);

            return new MethodResponse(0);
        }

        #endregion

        public async Task UpdateTwinAsync(Machine machine)
        {
            // Utwórz nowy obiekt TwinCollection do przechowywania właściwości raportowanych
            var reportedProperties = new TwinCollection();

            // Dodaj właściwości raportowane "DeviceErrors" i "LastErrorDate" do kolekcji
            reportedProperties["DeviceErrors"] = machine.DeviceError;
            reportedProperties["LastErrorDate"] = DateTime.Now;

            // Wywołaj metodę UpdateReportedPropertiesAsync na kliencie urządzenia, aby zaktualizować właściwości raportowane
            await machine.DeviceClient.UpdateReportedPropertiesAsync(reportedProperties);
        }

        public async Task SetTwinAsync(Machine machine)
        {
            // Utwórz nowy obiekt TwinCollection do przechowywania właściwości raportowanych
            var reportedProperties = new TwinCollection();

            // Dodaj właściwości raportowane "DeviceErrors" i "ProductionRate" do kolekcji
            reportedProperties["DeviceErrors"] = machine.DeviceError;
            reportedProperties["ProductionRate"] = machine.Rate;

            // Jeśli urządzenie ma błąd, dodaj właściwość raportowaną "LastErrorDate" do kolekcji
            if (machine.DeviceError != 0)
            {
                reportedProperties["LastErrorDate"] = DateTime.Now;
            }

            // Wywołaj metodę UpdateReportedPropertiesAsync na kliencie urządzenia, aby zaktualizować właściwości raportowane
            await machine.DeviceClient.UpdateReportedPropertiesAsync(reportedProperties);

        }
    }
}
