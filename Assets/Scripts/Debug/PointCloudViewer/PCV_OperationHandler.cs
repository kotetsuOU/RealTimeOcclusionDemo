using UnityEngine;

public class PCV_OperationHandler : MonoBehaviour
{
    [SerializeField] private PCV_Settings settings;

    private void Awake()
    {
        if (settings == null) settings = GetComponent<PCV_Settings>();
    }

    private void OnValidate()
    {
        if (settings == null) settings = GetComponent<PCV_Settings>();
    }

    public void ExecuteVoxelDensityFilter(PCV_DataManager dataManager)
    {
        if (CheckDependencies(dataManager))
        {
            PCV_DensityFilter.Execute(dataManager, settings);
        }
    }

    public void ExecuteNeighborFilter(PCV_DataManager dataManager)
    {
        if (CheckDependencies(dataManager))
        {
            PCV_NeighborFilter.Execute(dataManager, settings, this);
        }
    }

    public void ExecuteMorphologyOperation(PCV_DataManager dataManager)
    {
        if (CheckDependencies(dataManager))
        {
            PCV_MorphologyFilter.Execute(dataManager, settings);
        }
    }

    public void ExecuteDensityComplementation(PCV_DataManager dataManager)
    {
        if (CheckDependencies(dataManager))
        {
            PCV_DensityComplementation.Execute(dataManager, settings);
        }
    }

    private bool CheckDependencies(PCV_DataManager dataManager)
    {
        if (settings == null)
        {
            UnityEngine.Debug.LogError("[PCV_OperationHandler] Settingsコンポーネントが見つかりません。");
            return false;
        }
        if (dataManager == null)
        {
            UnityEngine.Debug.LogError("[PCV_OperationHandler] DataManagerが渡されていません (nullです)。");
            return false;
        }
        return true;
    }
}