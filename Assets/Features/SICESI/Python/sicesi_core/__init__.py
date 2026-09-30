"""
SICESI Python Suite: コアライブラリ
"""

from .constants import (
    COLOR_CORRECT, COLOR_OCC_MATCH, COLOR_OVER_OCC, COLOR_UNDER_OCC, COLOR_BG,
    DEFAULT_BINARY_THRESHOLD
)

from .pattern_lut import (
    compress_indices, z_to_hex, hex_to_lut, occ_breakdown_str, class_breakdown_str,
    build_256_lookup, RULES_20_CANDIDATES
)

from .metrics import (
    UnionMaskMetrics, evaluate_lut_extended
)

from .visualizer import (
    OcclusionDiffVisualizer, export_all_diff_maps
)

from .dataset_loader import (
    load_dataset, save_pattern_vo_csv, auto_generate_missing_pattern_counts,
    find_first_image, resolve_gt_dir, parse_condition_metadata, natural_sort_key
)

from .optimizers import (
    optimize_fixed_8, optimize_fixed_20,
    optimize_weighted_iou_milp, optimize_single_iou_lut, get_individual_optima_36_and_256,
    local_search_36_iou, local_search_256_iou, PatternOptimizationPipeline
)

from .report_exporter import (
    OptimizationReportExporter
)

from .validator import (
    validate_hierarchy_inequalities
)

__all__ = [
    # constants
    "COLOR_CORRECT", "COLOR_OCC_MATCH", "COLOR_OVER_OCC", "COLOR_UNDER_OCC", "COLOR_BG",
    "DEFAULT_BINARY_THRESHOLD",
    # pattern_lut
    "compress_indices", "z_to_hex", "hex_to_lut", "occ_breakdown_str", "class_breakdown_str",
    "build_256_lookup", "RULES_20_CANDIDATES",
    # metrics
    "UnionMaskMetrics", "evaluate_lut_extended",
    # visualizer
    "OcclusionDiffVisualizer", "export_all_diff_maps",
    # dataset_loader
    "load_dataset", "save_pattern_vo_csv", "auto_generate_missing_pattern_counts",
    "find_first_image", "resolve_gt_dir", "parse_condition_metadata", "natural_sort_key",
    # optimizers
    "optimize_fixed_8", "optimize_fixed_20",
    "optimize_weighted_iou_milp", "optimize_single_iou_lut", "get_individual_optima_36_and_256",
    "local_search_36_iou", "local_search_256_iou", "PatternOptimizationPipeline",
    # report_exporter
    "OptimizationReportExporter",
    # validator
    "validate_hierarchy_inequalities",
]
