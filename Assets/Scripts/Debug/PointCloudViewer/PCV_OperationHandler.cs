using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

public class PCV_OperationHandler : MonoBehaviour
{
    [SerializeField] private PCV_Settings settings;
    [SerializeField] private PCV_DataManager dataManager;

    public void ExecuteVoxelDensityFilter()
    {
        PCV_DensityFilter.Execute(dataManager, settings);
    }

    public void ExecuteNoiseFilter()
    {
        PCV_NoiseFilter.Execute(dataManager, settings, this);
    }

    public void ExecuteMorphologyOperation()
    {
        PCV_MorphologyFilter.Execute(dataManager, settings);
    }

    public void ExecuteDensityComplementation()
    {
        PCV_DensityComplementation.Execute(dataManager, settings);
    }
}