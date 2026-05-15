using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GposeCameraSaver.Services;

public sealed class TerritoryNameService
{
    private readonly IClientState clientState;
    private readonly IDataManager dataManager;

    public TerritoryNameService(IClientState clientState, IDataManager dataManager)
    {
        this.clientState = clientState;
        this.dataManager = dataManager;
    }

    public uint TerritoryType => clientState.TerritoryType;

    public uint MapId => clientState.MapId;

    public string TerritoryName
    {
        get
        {
            try
            {
                return dataManager.GetExcelSheet<TerritoryType>().TryGetRow(clientState.TerritoryType, out var row)
                    ? row.PlaceName.Value.Name.ToString()
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
