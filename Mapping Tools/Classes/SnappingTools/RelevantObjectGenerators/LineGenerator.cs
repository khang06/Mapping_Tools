﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mapping_Tools.Classes.BeatmapHelper;
using Mapping_Tools.Classes.MathUtil;

namespace Mapping_Tools.Classes.SnappingTools.RelevantObjectGenerators {
    class LineGenerator : IGenerateRelevantObjectsFromRelevantPoints {
        public bool IsActive { get; set; }
        public string Name => "Line Generator";
        public GeneratorType GeneratorType => GeneratorType.Polygons;

        public List<IRelevantObject> GetRelevantObjects(List<RelevantPoint> objects) {
            List<IRelevantObject> newObjects = new List<IRelevantObject>();

            for (int i = 0; i < objects.Count; i++) {
                for (int k = i + 1; k < objects.Count; k++) {
                    var obj1 = objects[i];
                    var obj2 = objects[k];

                    Line line = new Line(obj1.child, obj2.child);
                    newObjects.Add(new RelevantLine(line));
                }
            }

            return newObjects;
        }
    }
}
