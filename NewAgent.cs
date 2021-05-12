
using Newtonsoft.Json;
using Pulumi;
using Pulumi.AzureNative.Network;
using System.Collections.Generic;
using System.Text.Json;
using System.Net; 

class NewAgent : Pulumi.Stack
{
    Pulumi.Config _config;

    #region Outputs
    [Output]
    public Output<string>? ResourceGroupName { get; set;}
    [Output] 
    public Output<string>? IpAddress { get; set; }
    #endregion

    public NewAgent()
    {
        _config = new Config("ado");

        #pragma warning disable
        Pulumi.InputMap<string> tags = JsonConvert.DeserializeObject<Dictionary<string, string>>(_config.RequireObject<JsonElement>("tags").ToString());

        #region AzureNative.Resources.ResourceGroup (-rg)
        var resourceGroup = new Pulumi.AzureNative.Resources.ResourceGroup($"{_config.Require("resourceGroup")}-{_config.Require("env")}-rg-", new Pulumi.AzureNative.Resources.ResourceGroupArgs
        {
            Location = _config.Require("location"),
            Tags = tags,
        });
        this.ResourceGroupName = resourceGroup.Name;
        #endregion

        #region NetworkSecurityGroup (-nsg)
        // NSGs can be connected to Subnets or Network cards (NICs).
        // When applied to the subnet, the NSG applies to all VMs on that subnet. If you apply only to the NIC card, just that one VM is affected.
        string myIp = new System.Net.WebClient().DownloadString("https://api.ipify.org");
        var networkSecurityGroup = new Pulumi.AzureNative.Network.NetworkSecurityGroup($"{_config.Require("vnet.name")}-{_config.Require("env")}-nsg-", new Pulumi.AzureNative.Network.NetworkSecurityGroupArgs
        {
            ResourceGroupName = resourceGroup.Name,
            SecurityRules = 
            {
                new Pulumi.AzureNative.Network.Inputs.SecurityRuleArgs 
                {
                Name = "allow-rdp",
                Protocol = SecurityRuleProtocol.Tcp,
                SourcePortRange = "*",
                DestinationPortRange ="3389",
                SourceAddressPrefix = myIp,
                DestinationAddressPrefix = "*",
                Access = "Allow",
                Priority = 300,
                Direction = "Inbound",
                },

                new Pulumi.AzureNative.Network.Inputs.SecurityRuleArgs 
                {
                Name = "allow-http", // _config.Require("nsg.name"),
                Protocol = SecurityRuleProtocol.Tcp,
                SourcePortRange = "*",
                DestinationPortRange ="443",
                SourceAddressPrefix ="*",
                DestinationAddressPrefix = "*",
                Access = "Allow",
                Priority = 301,
                Direction = "Inbound",
                },

            },
        });
        #endregion 

        #region VirtualNetwork (-vnet)
        var network = new VirtualNetwork($"{_config.Require("vnet.name")}-{_config.Require("env")}-vnet-",
            new VirtualNetworkArgs
            {
                AddressSpace = new Pulumi.AzureNative.Network.Inputs.AddressSpaceArgs
                {
                    AddressPrefixes = 
                    {
                        _config.Require("vnet.cidr"),
                    },
                },
                Location = _config.Require("location"),
                ResourceGroupName = resourceGroup.Name,
                Subnets = 
                {
                    new Pulumi.AzureNative.Network.Inputs.SubnetArgs
                    {
                        AddressPrefix = _config.Require("bastion.cidr"),
                        Name = _config.Require("AzureBastionSubnet"),
                        NetworkSecurityGroup = new Pulumi.AzureNative.Network.Inputs.NetworkSecurityGroupArgs
                        {
                            Id = networkSecurityGroup.Id,
                        },
                    },
                    new Pulumi.AzureNative.Network.Inputs.SubnetArgs
                    {
                        AddressPrefix = _config.Require("snet.cidr"),
                        Name = _config.Require("snet.name"),
                        NetworkSecurityGroup = new Pulumi.AzureNative.Network.Inputs.NetworkSecurityGroupArgs
                        {
                            Id = networkSecurityGroup.Id,
                        },
                    },
                },
                VirtualNetworkName = "AzureDevOps",
                // VirtualNetworkPeerings
                EnableDdosProtection = false,
            }
        );
        #endregion

        #region PublicIp (-ip)
        var publicIp = new Pulumi.AzureNative.Network.PublicIPAddress ($"bastion-{_config.Require("env")}-ip-",new Pulumi.AzureNative.Network.PublicIPAddressArgs
            {
                PublicIPAddressVersion = "IPv4",
                PublicIPAllocationMethod = "Static",
                IdleTimeoutInMinutes = 4,
                Location = _config.Require("location"),
                ResourceGroupName = resourceGroup.Name,
                Sku = new Pulumi.AzureNative.Network.Inputs.PublicIPAddressSkuArgs
                {
                    Name = "Basic",
                    Tier = "Regional",
                },
            });
        #endregion

        #region NetworkInterface (-nic)
        var networkInterface = new Pulumi.AzureNative.Network.NetworkInterface($"{_config.Require("agent.name")}-{_config.Require("env")}-nic-", new Pulumi.AzureNative.Network.NetworkInterfaceArgs
        {
            // AdoAgent has size Standard_B2s, which is not compatible with  accelerated networking on network interface.
            // https://docs.microsoft.com/en-us/azure/virtual-network/create-vm-accelerated-networking-powershell
            // EnableAcceleratedNetworking = true, 
            IpConfigurations = 
            {
                new Pulumi.AzureNative.Network.Inputs.NetworkInterfaceIPConfigurationArgs
                {
                    Name = "ipconfig1",
                    Primary = true,
                    PrivateIPAllocationMethod = IPAllocationMethod.Dynamic,
                    PrivateIPAddressVersion = "IPv4",
                    Subnet = new Pulumi.AzureNative.Network.Inputs.SubnetArgs
                    {
                        #pragma warning disable
                        Id = network.Subnets.Apply(s => s[1].Id), // ToDo: get by name not idex
                    },
                },
            },
            Location = _config.Require("location"),
            ResourceGroupName = resourceGroup.Name,
        });
        #endregion 

        #region VirtualMachine (-vm)
        var vm = new Pulumi.AzureNative.Compute.VirtualMachine($"{_config.Require("agent.name")}-{_config.Require("env")}-vmi-",new Pulumi.AzureNative.Compute.VirtualMachineArgs
            {
                ResourceGroupName = resourceGroup.Name,
                HardwareProfile = new Pulumi.AzureNative.Compute.Inputs.HardwareProfileArgs {VmSize = _config.Require("vm.vmSize")},
                VmName = "AdoAgent",
                DiagnosticsProfile = new Pulumi.AzureNative.Compute.Inputs.DiagnosticsProfileArgs
                {
                    BootDiagnostics =  new Pulumi.AzureNative.Compute.Inputs.BootDiagnosticsArgs {Enabled = true}
                },
                NetworkProfile = new Pulumi.AzureNative.Compute.Inputs.NetworkProfileArgs 
                {
                    NetworkInterfaces = 
                    {
                        new Pulumi.AzureNative.Compute.Inputs.NetworkInterfaceReferenceArgs
                        {
                            Id = networkInterface.Id,
                            Primary = true,
                        },
                    }
                },
                StorageProfile = new Pulumi.AzureNative.Compute.Inputs.StorageProfileArgs
                {
                    ImageReference = new Pulumi.AzureNative.Compute.Inputs.ImageReferenceArgs
                    {
                        Publisher = "MicrosoftWindowsServer",
                        Offer = "WindowsServer",
                        Sku = "2019-Datacenter-with-Containers-smalldisk",
                        Version = "latest"
                    },

                    OsDisk = new Pulumi.AzureNative.Compute.Inputs.OSDiskArgs
                    {
                        OsType = Pulumi.AzureNative.Compute.OperatingSystemTypes.Windows,
                        Name = "system",
                        CreateOption = Pulumi.AzureNative.Compute.DiskCreateOptionTypes.FromImage,
                        Caching = Pulumi.AzureNative.Compute.CachingTypes.ReadWrite,
                        ManagedDisk = new Pulumi.AzureNative.Compute.Inputs.ManagedDiskParametersArgs
                        {
                            StorageAccountType = Pulumi.AzureNative.Compute.StorageAccountTypes.Standard_LRS,
                        },
                        DiskSizeGB = 100,
                    },

                    DataDisks = 
                    {
                        new Pulumi.AzureNative.Compute.Inputs.DataDiskArgs
                        {
                            Name = "Data",
                            CreateOption = Pulumi.AzureNative.Compute.DiskCreateOptionTypes.Empty,
                            DiskSizeGB = _config.RequireInt32("vm.diskSizeGB"),
                            Lun = 0,
                            Caching = Pulumi.AzureNative.Compute.CachingTypes.None,
                        },
                    },
                },
                OsProfile = new Pulumi.AzureNative.Compute.Inputs.OSProfileArgs
                {
                    ComputerName = _config.Require("agent.name"),
                    AdminUsername = _config.Require("admin.user"),
                    AdminPassword = _config.RequireSecret("admin.pw"),
                    WindowsConfiguration = new Pulumi.AzureNative.Compute.Inputs.WindowsConfigurationArgs 
                    {
                        ProvisionVMAgent = true,
                        EnableAutomaticUpdates = true,
                        PatchSettings = new Pulumi.AzureNative.Compute.Inputs.PatchSettingsArgs 
                        {
                            PatchMode = Pulumi.AzureNative.Compute.WindowsVMGuestPatchMode.AutomaticByOS,
                            EnableHotpatching = false,
                        },
                    },
                },
            }); // new CustomResourceOptions { DeleteBeforeReplace = true });
            #endregion

        #region BastionHost (-bas)
            // Azure Bastion is a fully managed PaaS service that provides secure and seamless RDP and SSH access to virtual machines through the Azure Portal.
            // A BastionHost is provisioned within a Virtual Network (VNet) to provid SSL access to all VMs in the VNet without exposure through public IP addresses.
            var bastionHost = new Pulumi.AzureNative.Network.BastionHost($"{_config.Require("vnet.name")}-{_config.Require("env")}-bas-", new Pulumi.AzureNative.Network.BastionHostArgs
            {
                Location = resourceGroup.Location,
                ResourceGroupName = resourceGroup.Name,
                IpConfigurations = 
                {
                    new Pulumi.AzureNative.Network.Inputs.BastionHostIPConfigurationArgs
                    {
                        Name = "bastionHostIpConfiguration",
                        PublicIPAddress = new Pulumi.AzureNative.Network.Inputs.SubResourceArgs
                        {
                            Id = publicIp.IpAddress,
                        },
                        Subnet = new Pulumi.AzureNative.Network.Inputs.SubResourceArgs
                        {
                            Id = network.Subnets.Apply(s => s[0].Id),
                        },
                    },
                },
            });
            #endregion
    }
}
