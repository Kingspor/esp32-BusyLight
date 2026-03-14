# WPF Migration Analysis

## Current State

BusyLight uses **.NET 8 WinForms**. The UI is built programmatically (no Designer-generated XAML) with dynamic control creation in C# code.

## Why WPF?

| Area | WinForms (current) | WPF (target) |
|---|---|---|
| **Data binding** | Manual: read/write controls in code | MVVM: controls bound to ViewModels |
| **Dynamic rows** | Created in C# `AddPresenceRow()` | `ItemsControl` / `DataGrid` with `ItemTemplate` |
| **Theming** | System colors only | Full resource dictionaries; dark mode easy |
| **DPI scaling** | `AutoScaleMode.Font` (approximate) | Native vector rendering, perfect at all DPIs |
| **Animations** | Manual timers / custom paint | Built-in `Storyboard`, `VisualStateManager` |
| **Color wheel** | Custom `OnPaint` in `ColorWheelForm` | Can reuse as `UserControl` or swap for WPF equivalent |

## What Stays the Same

All non-UI code requires **zero changes**:
- `BleService`, `GraphService`, `ConfigurationService`
- All models (`AppSettings`, `LedCommand`, `PresenceSettings`, …)
- Firmware, GitHub Actions, `.gitignore`, publish profile

## Migration Scope

| Component | Effort | Notes |
|---|---|---|
| `TrayApplication.cs` | Low | Replace `ApplicationContext` with WPF `Application`; tray via `TaskbarIcon` (Hardcodet.NotifyIcon.Wpf) |
| `SettingsForm` | High | Largest form — rebuild as WPF `Window` with MVVM |
| `ColorWheelForm` | Medium | Custom paint → WPF `UserControl` with `DrawingContext` |
| `BlePickerForm` | Low | Simple list — trivial in WPF |
| `StatusBar` | None | Already a `StatusStrip` — becomes WPF `StatusBar` |

**Total estimate:** 3–5 days for a clean port with proper MVVM.

## Recommended Architecture (WPF)

```
BusyLight.Wpf/
├── App.xaml / App.xaml.cs          (WPF Application, tray icon init)
├── ViewModels/
│   ├── MainViewModel.cs            (presence, BLE state, override)
│   └── PresenceRowViewModel.cs     (one per presence map entry)
├── Views/
│   ├── SettingsWindow.xaml         (combined status + settings)
│   ├── BlePickerWindow.xaml
│   └── Controls/
│       └── ColorWheelControl.xaml
└── Converters/
    └── BleStateToColorConverter.cs
```

### MVVM Example — Presence Row

```csharp
public class PresenceRowViewModel : ObservableObject
{
    [ObservableProperty] private Color _color;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _brightness;
    [ObservableProperty] private int mode;
    // CommunityToolkit.Mvvm generates INotifyPropertyChanged boilerplate
}
```

```xml
<!-- replaces 80 lines of AddPresenceRow() -->
<DataGrid ItemsSource="{Binding PresenceRows}" AutoGenerateColumns="False">
    <DataGrid.Columns>
        <DataGridCheckBoxColumn  Header="Aktiv"    Binding="{Binding Enabled}" />
        <DataGridTemplateColumn  Header="Farbe">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <Rectangle Width="28" Height="20" Fill="{Binding Color, Converter={...}}"
                               MouseLeftButtonDown="OpenColorPicker"/>
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
        <!-- R, G, B, Brightness, Mode, Speed … -->
    </DataGrid.Columns>
</DataGrid>
```

## NuGet Packages to Add

| Package | Purpose |
|---|---|
| `Hardcodet.NotifyIcon.Wpf` | Tray icon for WPF (replaces `NotifyIcon`) |
| `CommunityToolkit.Mvvm` | `ObservableObject`, `RelayCommand`, source generators |

## Migration Steps

1. Create `BusyLight.Wpf` project alongside existing `BusyLight` WinForms project
2. Add project reference to a shared `BusyLight.Core` library (models + services — extracted from current project)
3. Port forms one by one; run both apps in parallel during transition
4. When all forms are ported and tested, delete the WinForms project
5. Update `BusyLight.csproj` references and publish profile

## Decision

**Recommendation:** Proceed with WPF migration after v1.0.0 is released.
The functional codebase is now stable and well-tested; the migration can be done incrementally without breaking the firmware or backend.
