namespace BricsAI.McpServer.Mock
{
    /// <summary>
    /// Builds a plausible sample exhibition drawing so every tool has real data to operate on:
    /// standard A2Z layers already present, a handful of vendor layers awaiting classification
    /// (one obvious-by-name, one obvious-by-geometry, one genuinely ambiguous), a booth grid with
    /// one intentionally unnumbered booth, and leftovers from a "previous proofing run" for
    /// testing cleanup.
    /// </summary>
    public static class MockDrawingSeeder
    {
        public static MockAcadApplication CreateSeededApplication()
        {
            var app = new MockAcadApplication();
            var doc = app.ActiveDocument;

            foreach (var name in new[]
            {
                "0", "Defpoints", "Expo_BoothOutline", "Expo_BoothNumber", "Expo_Building",
                "Expo_Markings", "Expo_View2", "Expo_Column", "Expo_NES",
                "Expo_MaxBoothOutline", "Expo_MaxBoothNumber"
            })
            {
                doc.Layers.Add(name);
            }

            foreach (var name in new[] { "A-WALL-INT", "G-COLS", "SHOW-TITLE", "0-EL-1", "MISC-1", "Deleted_OldVendorJunk" })
            {
                doc.Layers.Add(name);
            }

            // 5 booth outlines (10x10 squares, area 100 -> within the 90-150 booth-box heuristic).
            // Booth #4 (0-indexed) is intentionally left without a number to exercise
            // count_empty_booths / select_empty_booths.
            for (int i = 0; i < 5; i++)
            {
                double x0 = i * 20, y0 = 0, x1 = x0 + 10, y1 = 10;
                doc.Entities.Add(new MockEntity
                {
                    DxfType = "LWPOLYLINE",
                    ObjectName = "AcDbPolyline",
                    Layer = "Expo_BoothOutline",
                    Closed = true,
                    Area = 100,
                    Coordinates = new[] { x0, y0, x1, y0, x1, y1, x0, y1 },
                    BoundMin = new[] { x0, y0, 0 },
                    BoundMax = new[] { x1, y1, 0 }
                });

                if (i != 4)
                {
                    doc.Entities.Add(new MockEntity
                    {
                        DxfType = "TEXT",
                        ObjectName = "AcDbText",
                        Layer = "Expo_BoothNumber",
                        TextString = $"B00{i + 1}",
                        InsertionPoint = new[] { x0 + 5, y0 + 5, 0 }
                    });
                }
            }

            // A-WALL-INT: obvious-by-name vendor layer -> should classify to Expo_Building.
            doc.Entities.Add(new MockEntity
            {
                DxfType = "LWPOLYLINE",
                ObjectName = "AcDbPolyline",
                Layer = "A-WALL-INT",
                Closed = true,
                Area = 4000,
                Coordinates = new double[] { 0, -20, 200, -20, 200, -15, 0, -15 },
                BoundMin = new double[] { 0, -20, 0 },
                BoundMax = new double[] { 200, -15, 0 }
            });
            doc.Entities.Add(new MockEntity { DxfType = "LINE", ObjectName = "AcDbLine", Layer = "A-WALL-INT" });
            doc.Entities.Add(new MockEntity { DxfType = "SPLINE", ObjectName = "AcDbSpline", Layer = "A-WALL-INT" });

            // G-COLS: obvious-by-geometry vendor layer (small circles) -> should classify to Expo_Column.
            for (int i = 0; i < 3; i++)
            {
                doc.Entities.Add(new MockEntity
                {
                    DxfType = "CIRCLE",
                    ObjectName = "AcDbCircle",
                    Layer = "G-COLS",
                    Area = 12,
                    BoundMin = new double[] { i * 30, -30, 0 },
                    BoundMax = new double[] { i * 30 + 4, -26, 0 }
                });
            }

            // SHOW-TITLE: obvious-by-name -> should classify to Expo_Markings.
            doc.Entities.Add(new MockEntity { DxfType = "TEXT", ObjectName = "AcDbText", Layer = "SHOW-TITLE", TextString = "Annual Trade Show 2026" });

            // 0-EL-1: name is ambiguous, but geometry (block + hatch) hints electrical -> Expo_View2.
            doc.Entities.Add(new MockEntity { DxfType = "INSERT", ObjectName = "AcDbBlockReference", Layer = "0-EL-1", Name = "ELEC_OUTLET" });
            doc.Entities.Add(new MockEntity { DxfType = "HATCH", ObjectName = "AcDbHatch", Layer = "0-EL-1" });

            // MISC-1: genuinely ambiguous name AND generic geometry -> exercises the uncertain path.
            doc.Entities.Add(new MockEntity { DxfType = "LINE", ObjectName = "AcDbLine", Layer = "MISC-1" });
            doc.Entities.Add(new MockEntity { DxfType = "LINE", ObjectName = "AcDbLine", Layer = "MISC-1" });

            // Leftover from a "previous proofing run" -> for clean_deleted_layers / delete_layers_by_prefix testing.
            doc.Entities.Add(new MockEntity { DxfType = "LWPOLYLINE", ObjectName = "AcDbPolyline", Layer = "Deleted_OldVendorJunk" });

            // Oversized annotation on a standard layer, whitelist-explode target for prepare_geometry.
            doc.Entities.Add(new MockEntity { DxfType = "MULTILEADER", ObjectName = "AcDbMLeader", Layer = "Expo_Markings" });

            return app;
        }
    }
}
