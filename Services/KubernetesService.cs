using k8s;
using k8s.Models;

namespace SmarthomeApi.Services;

public interface IKubernetesService
{
    Task<bool> ApplyYamlAsync(string yaml);
    Task<bool> DeleteDeploymentAsync(string name);
    Task<PodStatusInfo?> GetPodStatusAsync(string deploymentName);
}

public class PodStatusInfo
{
    public bool IsRunning { get; set; }
    public string? Status { get; set; }
}

public class KubernetesService : IKubernetesService
{
    private readonly IKubernetes _client;
    private readonly ILogger<KubernetesService> _logger;
    private readonly string? _imagePullSecret;
    private const string Namespace = "smarthome";

    public KubernetesService(ILogger<KubernetesService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _imagePullSecret = configuration["Kubernetes:ImagePullSecret"];

        // Use in-cluster config when running in K8s, fallback to kubeconfig
        try
        {
            _client = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
            _logger.LogInformation("Using in-cluster Kubernetes configuration");
        }
        catch
        {
            // Check for configured context, otherwise use default
            var kubeContext = configuration["Kubernetes:Context"];

            KubernetesClientConfiguration config;
            if (!string.IsNullOrEmpty(kubeContext))
            {
                config = KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: kubeContext);
                _logger.LogInformation("Using kubeconfig with context: {Context}", kubeContext);
            }
            else
            {
                config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
                _logger.LogInformation("Using kubeconfig with default context: {Context}", config.CurrentContext);
            }

            _client = new Kubernetes(config);
        }

        if (!string.IsNullOrEmpty(_imagePullSecret))
        {
            _logger.LogInformation("Using image pull secret: {Secret}", _imagePullSecret);
        }
    }

    public async Task<bool> ApplyYamlAsync(string yaml)
    {
        try
        {
            _logger.LogInformation("Applying YAML to Kubernetes cluster...");

            // Parse and apply each document in the YAML
            var documents = yaml.Split("---", StringSplitOptions.RemoveEmptyEntries);
            _logger.LogInformation("Found {Count} YAML documents to apply", documents.Length);

            foreach (var doc in documents)
            {
                var trimmedDoc = doc.Trim();
                if (string.IsNullOrWhiteSpace(trimmedDoc)) continue;

                // Determine the kind of resource and apply it
                if (trimmedDoc.Contains("kind: Deployment"))
                {
                    var deployment = KubernetesYaml.Deserialize<V1Deployment>(trimmedDoc);
                    _logger.LogInformation("Applying Deployment: {Name}", deployment.Metadata.Name);
                    await ApplyDeploymentAsync(deployment);
                }
                else if (trimmedDoc.Contains("kind: Service"))
                {
                    var service = KubernetesYaml.Deserialize<V1Service>(trimmedDoc);
                    _logger.LogInformation("Applying Service: {Name}", service.Metadata.Name);
                    await ApplyServiceAsync(service);
                }
                else if (trimmedDoc.Contains("kind: ConfigMap"))
                {
                    var configMap = KubernetesYaml.Deserialize<V1ConfigMap>(trimmedDoc);
                    _logger.LogInformation("Applying ConfigMap: {Name}", configMap.Metadata.Name);
                    await ApplyConfigMapAsync(configMap);
                }
                else
                {
                    _logger.LogWarning("Unsupported resource kind in YAML: {Doc}", trimmedDoc.Substring(0, Math.Min(100, trimmedDoc.Length)));
                }
            }

            _logger.LogInformation("Successfully applied all YAML documents");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply YAML: {Message}", ex.Message);
            return false;
        }
    }

    private async Task ApplyDeploymentAsync(V1Deployment deployment)
    {
        var name = deployment.Metadata.Name;
        var ns = deployment.Metadata.NamespaceProperty ?? Namespace;

        // Add image pull secret if configured
        if (!string.IsNullOrEmpty(_imagePullSecret))
        {
            deployment.Spec.Template.Spec.ImagePullSecrets ??= new List<V1LocalObjectReference>();

            // Only add if not already present
            if (!deployment.Spec.Template.Spec.ImagePullSecrets.Any(s => s.Name == _imagePullSecret))
            {
                deployment.Spec.Template.Spec.ImagePullSecrets.Add(
                    new V1LocalObjectReference { Name = _imagePullSecret });
                _logger.LogInformation("Added imagePullSecret '{Secret}' to deployment {Name}",
                    _imagePullSecret, name);
            }
        }

        try
        {
            await _client.AppsV1.ReplaceNamespacedDeploymentAsync(deployment, name, ns);
            _logger.LogInformation("Updated deployment: {Name}", name);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _client.AppsV1.CreateNamespacedDeploymentAsync(deployment, ns);
            _logger.LogInformation("Created deployment: {Name}", name);
        }
    }

    private async Task ApplyServiceAsync(V1Service service)
    {
        var name = service.Metadata.Name;
        var ns = service.Metadata.NamespaceProperty ?? Namespace;

        try
        {
            // Services need special handling - get existing first to preserve clusterIP
            var existing = await _client.CoreV1.ReadNamespacedServiceAsync(name, ns);
            service.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;
            service.Spec.ClusterIP = existing.Spec.ClusterIP;
            await _client.CoreV1.ReplaceNamespacedServiceAsync(service, name, ns);
            _logger.LogInformation("Updated service: {Name}", name);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _client.CoreV1.CreateNamespacedServiceAsync(service, ns);
            _logger.LogInformation("Created service: {Name}", name);
        }
    }

    private async Task ApplyConfigMapAsync(V1ConfigMap configMap)
    {
        var name = configMap.Metadata.Name;
        var ns = configMap.Metadata.NamespaceProperty ?? Namespace;

        try
        {
            await _client.CoreV1.ReplaceNamespacedConfigMapAsync(configMap, name, ns);
            _logger.LogInformation("Updated configmap: {Name}", name);
        }
        catch (k8s.Autorest.HttpOperationException ex)
            when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _client.CoreV1.CreateNamespacedConfigMapAsync(configMap, ns);
            _logger.LogInformation("Created configmap: {Name}", name);
        }
    }

    public async Task<bool> DeleteDeploymentAsync(string name)
    {
        try
        {
            // Delete deployment
            try
            {
                await _client.AppsV1.DeleteNamespacedDeploymentAsync(name, Namespace);
                _logger.LogInformation("Deleted deployment: {Name}", name);
            }
            catch (k8s.Autorest.HttpOperationException ex)
                when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Already deleted
            }

            // Delete associated service
            try
            {
                await _client.CoreV1.DeleteNamespacedServiceAsync(name, Namespace);
                _logger.LogInformation("Deleted service: {Name}", name);
            }
            catch (k8s.Autorest.HttpOperationException ex)
                when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No service or already deleted
            }

            // Delete associated configMap (named {module}-config)
            try
            {
                await _client.CoreV1.DeleteNamespacedConfigMapAsync($"{name}-config", Namespace);
                _logger.LogInformation("Deleted configmap: {Name}-config", name);
            }
            catch (k8s.Autorest.HttpOperationException ex)
                when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No configmap or already deleted
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete deployment: {Name}", name);
            return false;
        }
    }

    public async Task<PodStatusInfo?> GetPodStatusAsync(string deploymentName)
    {
        try
        {
            var pods = await _client.CoreV1.ListNamespacedPodAsync(
                Namespace,
                labelSelector: $"app={deploymentName}");

            var pod = pods.Items.FirstOrDefault();
            if (pod == null) return null;

            return new PodStatusInfo
            {
                IsRunning = pod.Status?.Phase == "Running",
                Status = pod.Status?.Phase
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pod status for: {Name}", deploymentName);
            return null;
        }
    }
}
