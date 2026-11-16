import math
import time
from shapely.geometry import Polygon, MultiPolygon
from shapely.ops import unary_union
from comtypes.client import CreateObject

def round_coordinates(coords, precision=2):
    return [round(coord, precision) for coord in coords]

def draw_polyline_from_point(acad, start_point, angle, length, layer):
    angle_rad = math.radians(angle)
    end_point = (
        round(start_point[0] + length * math.cos(angle_rad), 2),
        round(start_point[1] + length * math.sin(angle_rad), 2)
    )
    coords = [start_point[0], start_point[1], end_point[0], end_point[1]]
    polyline = acad.ModelSpace.AddLightWeightPolyline(coords)
    polyline.Layer = layer
    return polyline, end_point

def create_layer(acad, layer_name):
    layers = acad.Layers
    for layer in layers:
        if layer.Name == layer_name:
            return layer
    new_layer = layers.Add(layer_name)
    return new_layer

def is_closed_polyline(polyline):
    return polyline.Closed

def get_polygon_from_coordinates(coordinates):
    coordinates = round_coordinates(coordinates)
    points = [(coordinates[i], coordinates[i + 1]) for i in range(0, len(coordinates), 2)]
    return Polygon(points)

def add_polygon_to_acad(acad, polygon, layer):
    if not polygon.is_empty:
        coords = list(polygon.exterior.coords)
        flat_coords = [coord for point in coords for coord in round_coordinates((point[0], point[1]))]
        polyline = acad.ModelSpace.AddLightWeightPolyline(flat_coords)
        polyline.Layer = layer
        polyline.Closed = True

def process_entity(entity, retries=5, delay=1):
    for attempt in range(retries):
        try:
            if entity.ObjectName == 'AcDbPolyline' and is_closed_polyline(entity):
                coordinates = list(entity.Coordinates)
                coordinates = round_coordinates(coordinates)
                return coordinates
        except Exception as e:
            print(f"Error processing entity (attempt {attempt + 1} of {retries}): {e}")
            time.sleep(delay)
    return None

def main():
    acad = CreateObject("AutoCAD.Application")
    acad.Visible = True
    doc = acad.ActiveDocument

    # Cancel all operations
    doc.SendCommand('._CANCEL \n')
    time.sleep(1)

    # Regenerate drawing
    doc.SendCommand('._REGEN \n')
    time.sleep(1)

    # Get number of floors
    default_floors = 8
    try:
        floors = int(doc.Utility.GetInteger("Enter number of floors (default 8): "))
        if floors <= 0:
            floors = default_floors
    except:
        floors = default_floors

    shadow_length = floors * 3  # 3 meters per floor

    # Get shadow angle
    default_angle = 45.0
    try:
        shadow_angle = float(doc.Utility.GetReal("Enter shadow angle (default 45 degrees): "))
        if shadow_angle < 0 or shadow_angle > 360:
            shadow_angle = default_angle
    except:
        shadow_angle = default_angle

    # Create layers
    try:
        create_layer(doc, "Shd-tmp")
        create_layer(doc, "Shd-count-tmp")
        create_layer(doc, "Shd-outer-contours")
    except Exception as e:
        print(f"Error creating layers: {e}")
        return

    # Select objects
    try:
        selection = doc.Utility.GetEntity("Select closed polylines: ")
    except Exception as e:
        print(f"Error selecting objects: {e}")
        return

    polygons = []
    temp_polylines = []

    for entity in selection:
        coordinates = process_entity(entity)
        if coordinates:
            num_vertices = len(coordinates) // 2
            polyline_points = []
            for i in range(num_vertices):
                x = coordinates[2 * i]
                y = coordinates[2 * i + 1]
                start_point = (x, y)
                polyline, end_point = draw_polyline_from_point(doc, start_point, shadow_angle, shadow_length, "Shd-tmp")
                polyline_points.append((start_point, end_point))
                temp_polylines.append(polyline)

            for i in range(num_vertices):
                point1 = polyline_points[i][0]
                point2 = polyline_points[(i+1) % num_vertices][0]
                contour_points = [
                    point1[0], point1[1],
                    point2[0], point2[1],
                    polyline_points[(i+1) % num_vertices][1][0], polyline_points[(i+1) % num_vertices][1][1],
                    polyline_points[i][1][0], polyline_points[i][1][1]
                ]
                new_contour = doc.ModelSpace.AddLightWeightPolyline(contour_points)
                new_contour.Layer = "Shd-count-tmp"
                new_contour.Closed = True
                temp_polylines.append(new_contour)
                polygons.append(get_polygon_from_coordinates(contour_points))

    # Union and process polygons
    if polygons:
        unified_polygon = unary_union(MultiPolygon(polygons))
        add_polygon_to_acad(doc, unified_polygon, "Shd-outer-contours")

    # Cleanup temporary polylines
    for polyline in temp_polylines:
        try:
            polyline.Delete()
        except Exception as e:
            print(f"Error deleting polyline: {e}")

    print("Shadow generation completed.")

if __name__ == "__main__":
    main()