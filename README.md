# Using Azure Database for MySQL from Azure Kubernetes Service

**... or connecting efficiently and minimizing network latency to a minimum when working wich Azure Database for MySQL.**

I will demonstrate the following best practices when working with Azure Database for MySQL in a sample application in C# running in AKS, *but* these can be applied to any application, running at AKS or in another compute service.

The following demonstrates how bad the connection performance can be if you don't following our published best practices:
- use [Accelerated Networking](https://docs.microsoft.com/en-us/azure/mysql/concepts-aks#accelerated-networking)
- use [Connection Pooling or Persistent Connections](https://docs.microsoft.com/en-us/azure/mysql/concepts-connectivity#connect-efficiently-to-azure-database-for-mysql)

## Creating AKS cluster with multiple node pools
 
We will use the [Azure CLI](https://docs.microsoft.com/en-us/azure/aks/use-multiple-node-pools) for creating an AKS cluster with two node pools.

One node pool will use VMs which have Accelerated Networking enabled, the other one will use VMs which don't support Accelerated Networking.

```bash
az login

# Create a resource group in East US
az group create --name myAKSMySQLResourceGroup --location westeurope

# Create the container registry
az acr create --resource-group myAKSMySQLResourceGroup --name myAKSMySQLAcr --sku Basic

# Create the AKS cluster
az aks create \
    --resource-group myAKSMySQLResourceGroup \
    --name myAKSMySQLCluster \
    --vm-set-type VirtualMachineScaleSets \
    --nodepool-name noaccpool \
    --node-count 1 \
    --node-vm-size Standard_D2s_v3 \
    --generate-ssh-keys \
    --attach-acr myAKSMySQLAcr \
    --load-balancer-sku standard

az aks nodepool add \
    --resource-group myAKSMySQLResourceGroup \
    --cluster-name myAKSMySQLCluster \
    --name accpool \
    --node-count 1 \
    --node-vm-size Standard_DS2_v2 

```

> VM Series D/Ds v3 only [supports Accelerated Networking](https://docs.microsoft.com/en-us/azure/virtual-network/create-vm-accelerated-networking-cli#limitations-and-constraints) on VM instances with 4 or more vCPUs.

You can test which VMs have Accelerated Networking enabled with the following:
```bash
az aks show --resource-group myAKSMySQLResourceGroup --name myAKSMySQLCluster --query "nodeResourceGroup"

az vmss list -g <node_resource_group> -o table

# See for that only for our second node pool EnableAcceleratedNetworking is true
az vmss nic list --resource-group <node_resource_group> --vmss-name aks-noaccpool-[]-vmss -o table
az vmss nic list --resource-group <node_resource_group> --vmss-name aks-accpool-[]-vmss -o table
```

## Create Azure Database for MySQL and configure VNet service endpoint
```bash
az mysql server create --resource-group myAKSMySQLResourceGroup --name myaksmysqldemoserver  --location westeurope --admin-user myadmin --admin-password <server_admin_password> --sku-name GP_Gen5_2 

# configure VNet service endpoint
az network vnet list -g <node_resource_group> -o table

az network vnet subnet update -n aks-subnet --vnet-name <aks_vnet> -g <node_resource_group> --service-endpoints Microsoft.SQL

az mysql server vnet-rule create \
    -g myAKSMySQLResourceGroup
    -s myaksmysqldemoserver 
    -n vnetRuleName
    --subnet /subscriptions/{SubID}/resourceGroups/{node_resource_group}/providers/Microsoft.Network/virtualNetworks/<aks_vnet>/subnets/aks-subnet

# If you want to run the test application locally with Azure Database for MySQL
# az mysql server firewall-rule create --resource-group myAKSMySQLResourceGroup --server myaksmysqldemoserver --name AllowMyIP --start-ip-address <my_ip> --end-ip-address <my_ip>
```

## Build and publish the test application to AKS

```bash
# Build and containerize test application
cd src
dotnet publish -c Release

cd ..
docker build -t myaksmysqlacr.azurecr.io/testapp .

# Publish container image
az acr login -n myAKSMySQLAcr
docker push myaksmysqlacr.azurecr.io/testapp

# Create two jobs in AKS with the test application
az aks get-credentials -n myAKSMySQLCluster  -g myAKSMySQLResourceGroup

kubectl create secret generic mysql --from-literal=connection='Server=myaksmysqldemoserver.mysql.database.azure.com;Port=3306;Uid=myadmin@myaksmysqldemoserver;Pwd=<server_admin_password>;SslMode=Preferred'

kubectl create -f jobs.yaml
```

## Get results

```bash
# show the logs of both scheduled pods to see the results
kubectl logs testapp-acc-
kubectl logs testapp-noacc-
```

# Results

As you can see the biggest difference brings [Connection Pooling](https://docs.microsoft.com/en-us/azure/mysql/concepts-connectivity#connect-efficiently-to-azure-database-for-mysql) (or persistent connections) and should be always used.

Because accelerated networking on a VM results in lower latency, reduced jitter, and decreased CPU utilization on the VM, it's also strongly recommend. Find more information about the internals and benefits at [Create a Linux virtual machine with Accelerated Networking using Azure CLI](https://docs.microsoft.com/en-us/azure/virtual-network/create-vm-accelerated-networking-cli#benefits).

## Connection Latency

|                                    | Connection Pooling: No | Connection Pooling: Yes |
|:----------------------------------:|:----------------------:|:-----------------------:|
|   **Accelerated Networking: No**  |        143.34ms        |          8.61ms         |
|  **Accelerated Networking: Yes**  |        141.57ms        |          7.09ms         |


## Command Latency (without Connection)

|                                    | Connection Pooling: No | Connection Pooling: Yes |
|:----------------------------------:|:----------------------:|:-----------------------:|
|  **Accelerated Networking: No**   |         4.25ms         |          3.54ms         |
|  **Accelerated Networking: Yes**  |         3.42ms         |          3.3ms          |


## Raw Results

### With Accelerated networking
```
Connection Pooling: False
Cold Connection Latency: 530ms
Cold Command Latency: 74ms
Warm Iterations: 100
Avg. Connection Latency: 141.57ms
Avg. Command Latency: 3.42ms

Connection Pooling: True
Cold Connection Latency: 165ms
Cold Command Latency: 3ms
Warm Iterations: 100
Avg. Connection Latency: 7.09ms
Avg. Command Latency: 3.3ms
```

#### Without Accelerated networking
```
Connection Pooling: False
Cold Connection Latency: 532ms
Cold Command Latency: 78ms
Warm Iterations: 100
Avg. Connection Latency: 143.34ms
Avg. Command Latency: 4.25ms

Connection Pooling: True
Cold Connection Latency: 182ms
Cold Command Latency: 5ms
Warm Iterations: 100
Avg. Connection Latency: 8.61ms
Avg. Command Latency: 3.54ms
```