using System.ComponentModel;
using System.Threading.Tasks;
using BricsAI.McpServer.Services;
using ModelContextProtocol.Server;

namespace BricsAI.McpServer.Tools
{
    [McpServerToolType]
    public static class GeometryTools
    {
        [McpServerTool(Name = "select_booth_boxes"), Description("Selects closed polylines matching booth-outline area heuristics; moves them to targetLayer if given, otherwise highlights them.")]
        public static Task<string> SelectBoothBoxes(ComClient comClient, StaComHost sta, [Description("Optional destination layer.")] string? targetLayer = null)
            => ToolExec.RunAsync(comClient, sta, targetLayer == null ? "NET:SELECT_BOOTH_BOXES" : $"NET:SELECT_BOOTH_BOXES:{targetLayer}");

        [McpServerTool(Name = "select_empty_booths"), Description("Ray-casts booth-number text against booth-outline polygons; selects (or moves to targetLayer) outlines with no matching number inside them.")]
        public static Task<string> SelectEmptyBooths(ComClient comClient, StaComHost sta, [Description("Optional destination layer.")] string? targetLayer = null)
            => ToolExec.RunAsync(comClient, sta, targetLayer == null ? "NET:SELECT_EMPTY_BOOTHS" : $"NET:SELECT_EMPTY_BOOTHS:{targetLayer}");

        [McpServerTool(Name = "count_empty_booths"), Description("Read-only audit: counts booth outlines in Expo_BoothOutline that have no corresponding Expo_BoothNumber text inside them. Works even on locked layers.")]
        public static Task<string> CountEmptyBooths(ComClient comClient, StaComHost sta)
            => ToolExec.RunAsync(comClient, sta, "NET:COUNT_EMPTY_BOOTHS");

        [McpServerTool(Name = "select_building_lines"), Description("Selects the largest-bounding-box closed polyline on a layer (building outline heuristic); moves it to targetLayer if given.")]
        public static Task<string> SelectBuildingLines(ComClient comClient, StaComHost sta, [Description("Optional destination layer.")] string? targetLayer = null)
            => ToolExec.RunAsync(comClient, sta, targetLayer == null ? "NET:SELECT_BUILDING_LINES" : $"NET:SELECT_BUILDING_LINES:{targetLayer}");

        [McpServerTool(Name = "select_columns"), Description("Selects small circles/inserts matching column-size heuristics; moves them to targetLayer if given.")]
        public static Task<string> SelectColumns(ComClient comClient, StaComHost sta, [Description("Optional destination layer.")] string? targetLayer = null)
            => ToolExec.RunAsync(comClient, sta, targetLayer == null ? "NET:SELECT_COLUMNS" : $"NET:SELECT_COLUMNS:{targetLayer}");

        [McpServerTool(Name = "select_utilities"), Description("Selects hatch entities matching utility/symbol heuristics; moves them to targetLayer if given.")]
        public static Task<string> SelectUtilities(ComClient comClient, StaComHost sta, [Description("Optional destination layer.")] string? targetLayer = null)
            => ToolExec.RunAsync(comClient, sta, targetLayer == null ? "NET:SELECT_UTILITIES" : $"NET:SELECT_UTILITIES:{targetLayer}");

        [McpServerTool(Name = "prepare_geometry"), Description(
            "Iteratively explodes non-standard entities and purges unresolvable geometry (up to 30 passes / 120 seconds). " +
            "Booth output layers are locked throughout so they are never affected. Long-running.")]
        public static Task<string> PrepareGeometry(ComClient comClient, StaComHost sta)
            => ToolExec.RunAsync(comClient, sta, "NET:PREPARE_GEOMETRY");

        [McpServerTool(Name = "explode_entities_by_type"), Description("Explodes all entities of a given type (e.g. SPLINE, 3D SOLID, MULTILEADER, MTEXT) across up to 10 passes.")]
        public static Task<string> ExplodeEntitiesByType(ComClient comClient, StaComHost sta, [Description("Entity type name, e.g. 'MULTILEADER'.")] string entityType)
            => ToolExec.RunAsync(comClient, sta, $"NET:QSELECT_EXPLODE:{entityType}");

        [McpServerTool(Name = "explode_with_booth_lock"), Description(
            "Locks the four booth output layers (Expo_BoothOutline, Expo_BoothNumber, Expo_MaxBoothOutline, Expo_MaxBoothNumber) " +
            "then iteratively explodes all non-standard entities (up to 30 passes / 120 seconds). " +
            "Booth entities are protected throughout. Does not erase remaining unexplodable entities. Long-running.")]
        public static Task<string> ExplodeWithBoothLock(ComClient comClient, StaComHost sta)
            => ToolExec.RunAsync(comClient, sta, "NET:EXPLODE_WITH_BOOTH_LOCK");

        [McpServerTool(Name = "delete_non_standard_entities"), Description("Erases every entity except a fixed whitelist of standard types (ARC, LINE, CIRCLE, ELLIPSE, POLYLINE, LWPOLYLINE, TEXT, SOLID).")]
        public static Task<string> DeleteNonStandardEntities(ComClient comClient, StaComHost sta)
            => ToolExec.RunAsync(comClient, sta, "NET:DELETE_NON_STANDARD");
    }
}
