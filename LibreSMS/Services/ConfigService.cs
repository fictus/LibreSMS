using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LibreSMS.Models;

namespace LibreSMS.Services
{
    public class ConfigService
    {
        private readonly string _configPath;
        private GatewayConfig _config = new();

        public ConfigService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _configPath = Path.Combine(appData, "smsgateway_config.json");
        }

        public GatewayConfig Config => _config;

        public async Task LoadAsync()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = await File.ReadAllTextAsync(_configPath);
                    _config = JsonSerializer.Deserialize<GatewayConfig>(json) ?? new GatewayConfig();
                }
            }
            catch
            {
                _config = new GatewayConfig();
            }
        }

        public async Task SaveAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Config save error: {ex.Message}");
            }
        }

        public void UpdateConfig(GatewayConfig newConfig)
        {
            _config = newConfig;
        }
    }
}
