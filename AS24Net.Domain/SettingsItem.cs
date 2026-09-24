using System.ComponentModel.DataAnnotations;

namespace AS24Net.Domain;

public class SettingsItem
{
    [Required]
    public string Name { get; set; } = string.Empty;
    
    public string Json { get; set; } = string.Empty;
}