﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

// Required APIs to use Bluetooth GATT
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

// Required APIs to use built in GUIDs
using Windows.Devices.Enumeration;

// Required APIs for buffer manipulation & async operations
using Windows.Storage.Streams;
using System.Threading.Tasks;

namespace BluetoothGATT
{
    /// <summary>
    /// Sample app that communicates with Bluetooth device using the GATT profile
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // Arrays for information that needs to be saved
        private byte[] baroCalibrationData;
        private GattDeviceService[] serviceList = new GattDeviceService[7];
        private GattCharacteristic[] activeCharacteristics = new GattCharacteristic[7];

        // IDs for Sensors
        const int NUM_SENSORS = 7;
        const int IR_SENSOR = 0;
        const int ACCELEROMETER = 1;
        const int HUMIDITY = 2;
        const int MAGNETOMETER = 3;
        const int BAROMETRIC_PRESSURE = 4;
        const int GYROSCOPE = 5;
        const int KEYS = 6;

        private DeviceWatcher deviceWatcher = null;

        //Handlers for device detection
        private TypedEventHandler<DeviceWatcher, DeviceInformation> handlerAdded = null;
        private TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> handlerUpdated = null;
        private TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> handlerRemoved = null;
        private TypedEventHandler<DeviceWatcher, Object> handlerEnumCompleted = null;
        
        string pinUserEntered;
        Windows.UI.Xaml.Controls.Primitives.FlyoutBase savedPairButtonFlyout;
        DevicePairingRequestedEventArgs pairingRequestedHandlerArgs;
        Deferral pairingRequestDeferral;
        private enum MessageType { YesNoMessage, OKMessage };
        public ObservableCollection<DeviceInformationDisplay> ResultCollection
        {
            get;
            private set;
        }

        public MainPage()
        {
            this.InitializeComponent();

            // Save the flyout so it doesn't pop up unless we want it
            savedPairButtonFlyout = PairButton.Flyout;
            PairButton.Flyout = null;                    

            UserOut.Text = "Select the Sensor for Pairing";

            ResultCollection = new ObservableCollection<DeviceInformationDisplay>();

            DataContext = this;

            StartWatcher();
        }

        ~MainPage()
        {
            StopWatcher();
        }

        private void StartWatcher()
        {            
            string aqsFilter;                       

            ResultCollection.Clear();

            // Request the IsPaired property so we can display the paired status in the UI
            string[] requestedProperties = { "System.Devices.Aep.IsPaired" };

            // Get the device selector chosen by the UI, then 'AND' it with the 'CanPair' property            
            //For bluetooth devices
            //aqsFilter = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"" + " AND System.Devices.Aep.CanPair:=System.StructuredQueryType.Boolean#True";

            //for bluetooth LE Devices
            aqsFilter = "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"" + " AND System.Devices.Aep.CanPair:=System.StructuredQueryType.Boolean#True";

            deviceWatcher = DeviceInformation.CreateWatcher(
                aqsFilter,
                requestedProperties,
                DeviceInformationKind.AssociationEndpoint
                );

            // Hook up handlers for the watcher events before starting the watcher

            handlerAdded = new TypedEventHandler<DeviceWatcher, DeviceInformation>(async (watcher, deviceInfo) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                await this.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    ResultCollection.Add(new DeviceInformationDisplay(deviceInfo));
                    UserOut.Text = "Found " + ResultCollection.Count.ToString() + " Bluetooth LE Devices";
                });
            });
            deviceWatcher.Added += handlerAdded;

            handlerUpdated = new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    // Find the corresponding updated DeviceInformation in the collection and pass the update object
                    // to the Update method of the existing DeviceInformation. This automatically updates the object
                    // for us.
                    foreach (DeviceInformationDisplay deviceInfoDisp in ResultCollection)
                    {
                        if (deviceInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            deviceInfoDisp.Update(deviceInfoUpdate);
                            break;
                        }
                    }
                });
            });
            deviceWatcher.Updated += handlerUpdated;

            handlerRemoved = new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    // Find the corresponding DeviceInformation in the collection and remove it
                    foreach (DeviceInformationDisplay deviceInfoDisp in ResultCollection)
                    {
                        if (deviceInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            ResultCollection.Remove(deviceInfoDisp);
                            break;
                        }
                    }                  
                });
            });
            deviceWatcher.Removed += handlerRemoved;

            handlerEnumCompleted = new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    UserOut.Text = "Found " + ResultCollection.Count.ToString() + " Bluetooth LE Devices";
                    //Bluetooth watcher expires in 30 seconds currently, Stop and Start again to keep detecting devices
                    StopWatcher();
                    StartWatcher();
                });
            });

            deviceWatcher.EnumerationCompleted += handlerEnumCompleted;            
            
            deviceWatcher.Start();            
        }

        private void StopWatcher()
        {
            if (null != deviceWatcher)
            {
                // First unhook all event handlers except the stopped handler. This ensures our
                // event handlers don't get called after stop, as stop won't block for any "in flight" 
                // event handler calls.  We leave the stopped handler as it's guaranteed to only be called
                // once and we'll use it to know when the query is completely stopped. 
                deviceWatcher.Added -= handlerAdded;
                deviceWatcher.Updated -= handlerUpdated;
                deviceWatcher.Removed -= handlerRemoved;
                deviceWatcher.EnumerationCompleted -= handlerEnumCompleted;

                if (DeviceWatcherStatus.Started == deviceWatcher.Status ||
                    DeviceWatcherStatus.EnumerationCompleted == deviceWatcher.Status)
                {
                    deviceWatcher.Stop();
                }
            }            
        }

        // Setup
        // Saves GATT service object in array
        private async Task<bool> init()
        {
            // Retrieve instances of the GATT services that we will use
            for (int i = 0; i < NUM_SENSORS; i++)
            {
                // Setting Service GUIDs
                // Built in enumerations are found in the GattServiceUuids class like this: GattServiceUuids.GenericAccess
                Guid BLE_GUID;
                if (i < 6)
                    BLE_GUID = new Guid("F000AA" + i + "0-0451-4000-B000-000000000000");
                else
                    BLE_GUID = new Guid("0000FFE0-0000-1000-8000-00805F9B34FB");

                // Retrieving and saving GATT services
                var services = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(BLE_GUID), null);
                if(services != null && services.Count > 0)
                {
                    if (services[0].IsEnabled)
                    {
                        GattDeviceService service = await GattDeviceService.FromIdAsync(services[0].Id);
                        if(service.Device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                        {
                            serviceList[i] = service;
                        }
                        else
                        {
                            return false;
                        }
                             
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            return true;
        }


        // ---------------------------------------------------
        //     Hardware Configuration Helper Functions
        // ---------------------------------------------------

        // Retrieve Barometer Calibration data
        private async void calibrateBarometer()
        {
            GattDeviceService gattService = serviceList[BAROMETRIC_PRESSURE];
            if (gattService != null)
            {
                // Set Barometer configuration to 2, so that calibration data is saved
                var characteristicList = gattService.GetCharacteristics(new Guid("F000AA42-0451-4000-B000-000000000000"));
                if (characteristicList != null)
                {
                    GattCharacteristic characteristic = characteristicList[0];

                    if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write))
                    {
                        var writer = new Windows.Storage.Streams.DataWriter();
                        writer.WriteByte((Byte)0x02);
                        await characteristic.WriteValueAsync(writer.DetachBuffer());
                    }
                }

                // Save Barometer calibration data
                characteristicList = gattService.GetCharacteristics(new Guid("F000AA43-0451-4000-B000-000000000000"));
                if (characteristicList != null)
                {
                    GattReadResult result = await characteristicList[0].ReadValueAsync(BluetoothCacheMode.Uncached);
                    baroCalibrationData = new byte[result.Value.Length];
                    DataReader.FromBuffer(result.Value).ReadBytes(baroCalibrationData);
                }
            }
        }

        // Set sensor update period 
        private async void setSensorPeriod(int sensor, int period)
        {
            GattDeviceService gattService = serviceList[sensor];
            if (sensor != KEYS && gattService != null)
            {
                var characteristicList = gattService.GetCharacteristics(new Guid("F000AA" + sensor + "3-0451-4000-B000-000000000000"));
                if (characteristicList != null)
                {
                    GattCharacteristic characteristic = characteristicList[0];

                    if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write))
                    {
                        var writer = new Windows.Storage.Streams.DataWriter();
                        // Accelerometer period = [Input * 10]ms
                        writer.WriteByte((Byte)(period / 10));
                        await characteristic.WriteValueAsync(writer.DetachBuffer());
                    }
                }
            }
        }

        // Enable and subscribe to specified GATT characteristic
        private async void enableSensor(int sensor)
        {
            GattDeviceService gattService = serviceList[sensor];
            if (gattService != null)
            {
                // Turn on notifications
                IReadOnlyList<GattCharacteristic> characteristicList;
                if (sensor >= 0 && sensor <= 5)
                    characteristicList = gattService.GetCharacteristics(new Guid("F000AA" + sensor + "1-0451-4000-B000-000000000000"));
                else
                    characteristicList = gattService.GetCharacteristics(new Guid("0000FFE1-0000-1000-8000-00805F9B34FB"));

                if (characteristicList != null)
                {
                    GattCharacteristic characteristic = characteristicList[0];
                    if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                    {
                        switch (sensor)
                        {
                            case (IR_SENSOR):
                                characteristic.ValueChanged += tempChanged;
                                IRTitle.Foreground = new SolidColorBrush(Colors.Green);
                                break;
                            case (ACCELEROMETER):
                                characteristic.ValueChanged += accelChanged;
                                AccelTitle.Foreground = new SolidColorBrush(Colors.Green);
                                setSensorPeriod(ACCELEROMETER, 250);
                                break;
                            case (HUMIDITY):
                                characteristic.ValueChanged += humidChanged;
                                HumidTitle.Foreground = new SolidColorBrush(Colors.Green);
                                break;
                            case (MAGNETOMETER):
                                characteristic.ValueChanged += magnoChanged;
                                MagnoTitle.Foreground = new SolidColorBrush(Colors.Green);
                                break;
                            case (BAROMETRIC_PRESSURE):
                                characteristic.ValueChanged += pressureChanged;
                                BaroTitle.Foreground = new SolidColorBrush(Colors.Green);
                                calibrateBarometer();
                                break;
                            case (GYROSCOPE):
                                characteristic.ValueChanged += gyroChanged;
                                GyroTitle.Foreground = new SolidColorBrush(Colors.Green);
                                break;
                            case (KEYS):
                                characteristic.ValueChanged += keyChanged;
                                KeyTitle.Foreground = new SolidColorBrush(Colors.Green);
                                break;
                            default:
                                break;
                        }

                        // Save a reference to each active characteristic, so that handlers do not get prematurely killed
                        activeCharacteristics[sensor] = characteristic;

                        // Set the notify enable flag
                        await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    }
                }

                // Turn on sensor
                if (sensor >= 0 && sensor <= 5)
                {
                    characteristicList = gattService.GetCharacteristics(new Guid("F000AA" + sensor + "2-0451-4000-B000-000000000000"));
                    if (characteristicList != null)
                    {
                        GattCharacteristic characteristic = characteristicList[0];
                        if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write))
                        {
                            var writer = new Windows.Storage.Streams.DataWriter();
                            // Special value for Gyroscope to enable all 3 axes
                            if (sensor == GYROSCOPE)
                                writer.WriteByte((Byte)0x07);
                            else
                                writer.WriteByte((Byte)0x01);

                            await characteristic.WriteValueAsync(writer.DetachBuffer());
                        }
                    }
                }
            }
        }

        // Disable notifications to specified GATT characteristic
        private async void disableSensor(int sensor)
        {
            GattDeviceService gattService = serviceList[sensor];
            if (gattService != null)
            {
                // Disable notifications
                IReadOnlyList<GattCharacteristic> characteristicList;
                if (sensor >= 0 && sensor <= 5)
                    characteristicList = gattService.GetCharacteristics(new Guid("F000AA" + sensor + "1-0451-4000-B000-000000000000"));
                else
                    characteristicList = gattService.GetCharacteristics(new Guid("0000FFE1-0000-1000-8000-00805F9B34FB"));

                if (characteristicList != null)
                {
                    GattCharacteristic characteristic = characteristicList[0];
                    if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                    {
                        GattCommunicationStatus status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                    }
                }
            }

            switch (sensor)
            {
                case (IR_SENSOR):
                    IRTitle.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case (ACCELEROMETER):
                    AccelTitle.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case (HUMIDITY):
                    HumidTitle.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case (MAGNETOMETER):
                    MagnoTitle.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case (BAROMETRIC_PRESSURE):
                    BaroTitle.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case (GYROSCOPE):
                    GyroTitle.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case (KEYS):
                    KeyTitle.Foreground = new SolidColorBrush(Colors.White);
                    KeyROut.Background = new SolidColorBrush(Colors.Red);
                    KeyLOut.Background = new SolidColorBrush(Colors.Red);
                    break;
                default:
                    break;
            }
            activeCharacteristics[sensor] = null;
        }




        // ---------------------------------------------------
        //             Pairing Process Handlers and Functions -- Begin
        // ---------------------------------------------------

        private async void PairButton_Click(object sender, RoutedEventArgs e)
        {
            PairButton.IsEnabled = false;

            DeviceInformationDisplay deviceInfoDisp = resultsListView.SelectedItem as DeviceInformationDisplay;

            DevicePairingKinds ceremoniesSelected = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ProvidePin | DevicePairingKinds.ConfirmPinMatch;
            DevicePairingProtectionLevel protectionLevel = DevicePairingProtectionLevel.Default;

            // Specify custom pairing with all ceremony types and protection level EncryptionAndAuthentication
            DeviceInformationCustomPairing customPairing = deviceInfoDisp.DeviceInformation.Pairing.Custom;

            customPairing.PairingRequested += PairingRequestedHandler;

            DevicePairingResult result = await customPairing.PairAsync(ceremoniesSelected, protectionLevel);

            if (result.Status == DevicePairingResultStatus.Paired)
            {
                // device is paired, set up the sensor Tag            
                UserOut.Text = "Setting up SensorTag";

                //It takes sometime for the services to be up and running, waiting for now
                await Task.Delay(TimeSpan.FromSeconds(40));

                bool okay = await init();
                if (okay)
                {
                    for (int i = 0; i < NUM_SENSORS; i++)
                    {
                        enableSensor(i);
                    }
                    UserOut.Text = "Sensors on!";
                }
                else
                {
                    UserOut.Text = "Something went wrong!";
                }
            }
            else
            {
                UserOut.Text = "Pairing Failed " + result.Status.ToString();
            }        
        }

        private void ResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeviceInformationDisplay deviceInfoDisp = (DeviceInformationDisplay)resultsListView.SelectedItem;

            if (null != deviceInfoDisp &&
                true == deviceInfoDisp.DeviceInformation.Pairing.CanPair &&
                false == deviceInfoDisp.DeviceInformation.Pairing.IsPaired)
            {
                PairButton.IsEnabled = true;
            }
            else
            {
                PairButton.IsEnabled = false;
            }
        }

        private async void PairingRequestedHandler(DeviceInformationCustomPairing sender,
                                     DevicePairingRequestedEventArgs args)
        {
            // Save the args for use in ProvidePin case
            pairingRequestedHandlerArgs = args;

            // Save the deferral away and complete it as necessary.
            if (args.PairingKind != DevicePairingKinds.DisplayPin)
            {
                pairingRequestDeferral = args.GetDeferral();
            }

            switch (args.PairingKind)
            {
                case DevicePairingKinds.ConfirmOnly:
                    // If users chooses "Yes" to confirmation dialog then call accept, otherwise don’t. If accept isn’t call, the async operation will completed with a failure.
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        string confirmationMessage = "You have requested to pair with the chosen device";
                        DisplayConfirmationPanelAndComplete(confirmationMessage, MessageType.OKMessage);
                    });                   break;

                case DevicePairingKinds.DisplayPin:
                    // We just show the PIN on this side. The ceremony is actually completed when the user enters the PIN
                    // on the target device
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            string confirmationMessage = "Please enter this PIN on the device you are pairing with: " + args.Pin;
                            DisplayConfirmationPanelAndComplete(confirmationMessage, MessageType.OKMessage);
                        });
                    }
                    break;

                case DevicePairingKinds.ProvidePin:
                    // A PIN may be shown on the target device and the user needs to enter the matching PIN on 
                    // this Windows device.
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        // Reattach the flyout
                        PairButton.Flyout = savedPairButtonFlyout;
                        pinEntryTextBox.Text = "";
                        PairButton.Flyout.ShowAt(PairButton);
                    });
                    break;

                case DevicePairingKinds.ConfirmPinMatch:
                    // We show the PIN here and the user responds with whether the PIN matches what they see
                    // on the target device. Response comes back and we set it on the PinComparePairingRequestedData
                    // then complete the deferral.
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        string confirmationMessage = "Does the PIN shown on the device you are trying to pair with match " + args.Pin + " ?";
                        DisplayConfirmationPanelAndComplete(confirmationMessage, MessageType.YesNoMessage);
                    });
                    break;
            }

            // Deferral completion, if requested, has been done in DisplayMessageDialogAndComplete or the Flyout PIN input handler
        }

        private async void DisplayConfirmationPanelAndComplete(string confirmationMessage, MessageType messageType)
        {
            // Use UI thread
            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {

                confirmationText.Text = confirmationMessage;
                if (messageType == MessageType.OKMessage)
                {
                    yesButton.Content = "OK";
                    noButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    yesButton.Content = "Yes";
                    noButton.Visibility = Visibility.Visible;
                }

                confirmationText.Visibility = Visibility.Visible;
                confirmationPanel.Visibility = Visibility.Visible;
            });
        }


        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            // Use the pairing
            if (pairingRequestedHandlerArgs != null)
            {
                pairingRequestedHandlerArgs.Accept();
                pairingRequestedHandlerArgs = null;
            }

            // Complete the deferral here
            if (pairingRequestDeferral != null)
            {
                pairingRequestDeferral.Complete();
                pairingRequestDeferral = null;
            }

            confirmationText.Visibility = Visibility.Collapsed;
            confirmationPanel.Visibility = Visibility.Collapsed;
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            // Complete the deferral here
            if (pairingRequestDeferral != null)
            {
                pairingRequestDeferral.Complete();
                pairingRequestDeferral = null;
            }

            confirmationText.Visibility = Visibility.Collapsed;
            confirmationPanel.Visibility = Visibility.Collapsed;
        }

        private void pinEntryTextBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {

            if (e.Key == Windows.System.VirtualKey.Enter && pinEntryTextBox.Text != "")
            {
                //  Close the flyout and save the PIN the user entered
                pinUserEntered = pinEntryTextBox.Text;
                PairButton.Flyout.Hide();

                // Use the pin
                if (pairingRequestedHandlerArgs != null)
                {
                    pairingRequestedHandlerArgs.Accept(pinUserEntered);
                    pairingRequestedHandlerArgs = null;
                }

                // Complete the deferral here
                if (pairingRequestDeferral != null)
                {
                    pairingRequestDeferral.Complete();
                    pairingRequestDeferral = null;
                }
            }
        }
        private async void UnpairButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceInformationDisplay deviceInfoDisp = resultsListView.SelectedItem as DeviceInformationDisplay;

            UnpairButton.IsEnabled = false;
            DeviceUnpairingResult dupr = await deviceInfoDisp.DeviceInformation.Pairing.UnpairAsync();

            UserOut.Text = "Unpairing result = " + dupr.Status.ToString();            

            UpdatePairingButtons();
        }

        private void UpdatePairingButtons()
        {
            DeviceInformationDisplay deviceInfoDisp = (DeviceInformationDisplay)resultsListView.SelectedItem;

            if (null != deviceInfoDisp &&
                deviceInfoDisp.DeviceInformation.Pairing.CanPair &&
                !deviceInfoDisp.DeviceInformation.Pairing.IsPaired)
            {
                PairButton.IsEnabled = true;
            }
            else
            {
                PairButton.IsEnabled = false;
            }

            if (null != deviceInfoDisp &&
                deviceInfoDisp.DeviceInformation.Pairing.IsPaired)
            {
                UnpairButton.IsEnabled = true;
            }
            else
            {
                UnpairButton.IsEnabled = false;
            }
        }

        // ---------------------------------------------------
        //             Pairing Process Handlers and Functions -- End
        // ---------------------------------------------------

        private void EnableButton_Click(object sender, RoutedEventArgs e)
        {
            if (SensorList.SelectedIndex >= 0)
            {
                enableSensor(SensorList.SelectedIndex);
            }
        }

        private void DisableButton_Click(object sender, RoutedEventArgs e)
        {
            if (SensorList.SelectedIndex >= 0)
            {
                disableSensor(SensorList.SelectedIndex);
            }            
        }

        // ---------------------------------------------------
        //           GATT Notification Handlers
        // ---------------------------------------------------

        // IR temperature change handler
        // Algorithm taken from http://processors.wiki.ti.com/index.php/SensorTag_User_Guide#IR_Temperature_Sensor
        async void tempChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);
            double AmbTemp = (double)(((UInt16)bArray[3] << 8) + (UInt16)bArray[2]);
            AmbTemp /= 128.0;

            Int16 temp = (Int16)(((UInt16)bArray[1] << 8) + (UInt16)bArray[0]);
            double Vobj2 = (double)temp;
            Vobj2 *= 0.00000015625;
            double Tdie = AmbTemp + 273.15;

            const double S0 = 5.593E-14;            // Calibration factor
            const double a1 = 1.75E-3;
            const double a2 = -1.678E-5;
            const double b0 = -2.94E-5;
            const double b1 = -5.7E-7;
            const double b2 = 4.63E-9;
            const double c2 = 13.4;
            const double Tref = 298.15;

            double S = S0 * (1 + a1 * (Tdie - Tref) + a2 * Math.Pow((Tdie - Tref), 2));
            double Vos = b0 + b1 * (Tdie - Tref) + b2 * Math.Pow((Tdie - Tref), 2);
            double fObj = (Vobj2 - Vos) + c2 * Math.Pow((Vobj2 - Vos), 2);
            double tObj = Math.Pow(Math.Pow(Tdie, 4) + (fObj / S), 0.25);

            tObj = (tObj - 273.15);
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                AmbTempOut.Text = string.Format("Chip:\t{0:0.0####}", AmbTemp);
                ObjTempOut.Text = string.Format("IR:  \t{0:0.0####}", tObj);
            });
        }

        // Accelerometer change handler
        // Algorithm taken from http://processors.wiki.ti.com/index.php/SensorTag_User_Guide#Accelerometer_2
        async void accelChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);

            double x = (SByte)bArray[0] / 64.0;
            double y = (SByte)bArray[1] / 64.0;
            double z = (SByte)bArray[2] / 64.0 * -1;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                RecTranslateTransform.X = x * 90;
                RecTranslateTransform.Y = y * -90;

                AccelXOut.Text = "X: " + x.ToString();
                AccelYOut.Text = "Y: " + y.ToString();
                AccelZOut.Text = "Z: " + z.ToString();
            });
        }

        // Humidity change handler
        // Algorithm taken from http://processors.wiki.ti.com/index.php/SensorTag_User_Guide#Humidity_Sensor_2
        async void humidChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);

            double humidity = (double)((((UInt16)bArray[1] << 8) + (UInt16)bArray[0]) & ~0x0003);
            humidity = (-6.0 + 125.0 / 65536 * humidity); // RH= -6 + 125 * SRH/2^16
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                HumidOut.Text = humidity.ToString();
            });
        }

        // Magnetometer change handler
        // Algorithm taken from http://processors.wiki.ti.com/index.php/SensorTag_User_Guide#Magnetometer
        async void magnoChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);

            Int16 data = (Int16)(((UInt16)bArray[1] << 8) + (UInt16)bArray[0]);
            double x = (double)data * (2000.0 / 65536);
            data = (Int16)(((UInt16)bArray[3] << 8) + (UInt16)bArray[2]);
            double y = (double)data * (2000.0 / 65536);
            data = (Int16)(((UInt16)bArray[5] << 8) + (UInt16)bArray[4]);
            double z = (double)data * (2000.0 / 65536);

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                MagnoXOut.Text = "X: " + x.ToString();
                MagnoYOut.Text = "Y: " + y.ToString();
                MagnoZOut.Text = "Z: " + z.ToString();
            });
        }

        // Barometric Pressure change handler
        // Algorithm taken from http://processors.wiki.ti.com/index.php/SensorTag_User_Guide#Barometric_Pressure_Sensor_2
        async void pressureChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            UInt16 c3 = (UInt16)(((UInt16)baroCalibrationData[5] << 8) + (UInt16)baroCalibrationData[4]);
            UInt16 c4 = (UInt16)(((UInt16)baroCalibrationData[7] << 8) + (UInt16)baroCalibrationData[6]);
            Int16 c5 = (Int16)(((UInt16)baroCalibrationData[9] << 8) + (UInt16)baroCalibrationData[8]);
            Int16 c6 = (Int16)(((UInt16)baroCalibrationData[11] << 8) + (UInt16)baroCalibrationData[10]);
            Int16 c7 = (Int16)(((UInt16)baroCalibrationData[13] << 8) + (UInt16)baroCalibrationData[12]);
            Int16 c8 = (Int16)(((UInt16)baroCalibrationData[15] << 8) + (UInt16)baroCalibrationData[14]);

            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);

            Int64 s, o, p, val;
            UInt16 Pr = (UInt16)(((UInt16)bArray[3] << 8) + (UInt16)bArray[2]);
            Int16 Tr = (Int16)(((UInt16)bArray[1] << 8) + (UInt16)bArray[0]);

            // Sensitivity
            s = (Int64)c3;
            val = (Int64)c4 * Tr;
            s += (val >> 17);
            val = (Int64)c5 * Tr * Tr;
            s += (val >> 34);

            // Offset
            o = (Int64)c6 << 14;
            val = (Int64)c7 * Tr;
            o += (val >> 3);
            val = (Int64)c8 * Tr * Tr;
            o += (val >> 19);

            // Pressure (Pa)
            p = ((Int64)(s * Pr) + o) >> 14;
            double pres = (double)p / 100;

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                BaroOut.Text = pres.ToString();
            });
        }

        // Gyroscope change handler
        // Algorithm taken from http://processors.wiki.ti.com/index.php/SensorTag_User_Guide#Gyroscope_2
        async void gyroChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);

            Int16 data = (Int16)(((UInt16)bArray[1] << 8) + (UInt16)bArray[0]);
            double x = (double)data * (500.0 / 65536);
            data = (Int16)(((UInt16)bArray[3] << 8) + (UInt16)bArray[2]);
            double y = (double)data * (500.0 / 65536);
            data = (Int16)(((UInt16)bArray[5] << 8) + (UInt16)bArray[4]);
            double z = (double)data * (500.0 / 65536);

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                GyroXOut.Text = "X: " + x.ToString();
                GyroYOut.Text = "Y: " + y.ToString();
                GyroZOut.Text = "Z: " + z.ToString();
            });
        }

        // Key press change handler
        // Algorithm taken from http://processors.wiki.ti.com/index.php/SensorTag_User_Guide#Simple_Key_Service
        async void keyChanged(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            byte[] bArray = new byte[eventArgs.CharacteristicValue.Length];
            DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(bArray);

            byte data = bArray[0];

            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if((data & 0x01) == 0x01)
                    KeyROut.Background = new SolidColorBrush(Colors.Green);
                else
                    KeyROut.Background = new SolidColorBrush(Colors.Red);

                if ((data & 0x02) == 0x02)
                    KeyLOut.Background = new SolidColorBrush(Colors.Green);
                else
                    KeyLOut.Background = new SolidColorBrush(Colors.Red);
            });
        }      
    }
}
