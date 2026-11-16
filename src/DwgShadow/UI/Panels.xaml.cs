using System.Windows.Controls;
using DwgShadow.Core;

namespace DwgShadow.UI
{
    public partial class BuildingsPanel : UserControl
    {
        public BuildingsPanel() { InitializeComponent(); }
    }

    public partial class LabelsPanel : UserControl
    {
        public LabelsPanel() { InitializeComponent(); }
    }

    public partial class LocationPanel : UserControl
    {
        class City
        {
            public string Ru, En;
            public double Lat, Lon, Utc;
            public override string ToString() => L.IsRussian ? Ru : En;
        }

        static readonly City[] CityList =
        {
            new City { Ru = "Москва", En = "Moscow", Lat = 55.7558, Lon = 37.6173, Utc = 3 },
            new City { Ru = "Санкт-Петербург", En = "Saint Petersburg", Lat = 59.9386, Lon = 30.3141, Utc = 3 },
            new City { Ru = "Казань", En = "Kazan", Lat = 55.7887, Lon = 49.1221, Utc = 3 },
            new City { Ru = "Нижний Новгород", En = "Nizhny Novgorod", Lat = 56.3269, Lon = 44.0059, Utc = 3 },
            new City { Ru = "Ростов-на-Дону", En = "Rostov-on-Don", Lat = 47.2357, Lon = 39.7015, Utc = 3 },
            new City { Ru = "Краснодар", En = "Krasnodar", Lat = 45.0355, Lon = 38.9753, Utc = 3 },
            new City { Ru = "Воронеж", En = "Voronezh", Lat = 51.6608, Lon = 39.2003, Utc = 3 },
            new City { Ru = "Самара", En = "Samara", Lat = 53.1959, Lon = 50.1002, Utc = 4 },
            new City { Ru = "Уфа", En = "Ufa", Lat = 54.7388, Lon = 55.9721, Utc = 5 },
            new City { Ru = "Пермь", En = "Perm", Lat = 58.0105, Lon = 56.2502, Utc = 5 },
            new City { Ru = "Екатеринбург", En = "Yekaterinburg", Lat = 56.8389, Lon = 60.6057, Utc = 5 },
            new City { Ru = "Тюмень", En = "Tyumen", Lat = 57.1530, Lon = 65.5343, Utc = 5 },
            new City { Ru = "Омск", En = "Omsk", Lat = 54.9885, Lon = 73.3242, Utc = 6 },
            new City { Ru = "Новосибирск", En = "Novosibirsk", Lat = 55.0084, Lon = 82.9357, Utc = 7 },
            new City { Ru = "Красноярск", En = "Krasnoyarsk", Lat = 56.0153, Lon = 92.8932, Utc = 7 },
            new City { Ru = "Иркутск", En = "Irkutsk", Lat = 52.2870, Lon = 104.3050, Utc = 8 },
            new City { Ru = "Владивосток", En = "Vladivostok", Lat = 43.1155, Lon = 131.8855, Utc = 10 },
            new City { Ru = "Минск", En = "Minsk", Lat = 53.9006, Lon = 27.5590, Utc = 3 },
            new City { Ru = "Алматы", En = "Almaty", Lat = 43.2389, Lon = 76.8897, Utc = 5 },
            new City { Ru = "Астана", En = "Astana", Lat = 51.1694, Lon = 71.4491, Utc = 5 },
            new City { Ru = "Лондон", En = "London", Lat = 51.5074, Lon = -0.1278, Utc = 0 },
            new City { Ru = "Берлин", En = "Berlin", Lat = 52.5200, Lon = 13.4050, Utc = 1 },
            new City { Ru = "Дубай", En = "Dubai", Lat = 25.2048, Lon = 55.2708, Utc = 4 },
            new City { Ru = "Нью-Йорк", En = "New York", Lat = 40.7128, Lon = -74.0060, Utc = -5 },
        };

        public LocationPanel()
        {
            InitializeComponent();
            Cities.Items.Add(L.T("city.pick"));
            foreach (City c in CityList) Cities.Items.Add(c);
            Cities.SelectedIndex = 0;
        }

        void Cities_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(Cities.SelectedItem is City c) || !(DataContext is DialogModel m)) return;
            m.S.Latitude = c.Lat;
            m.S.Longitude = c.Lon;
            m.S.UtcOffset = c.Utc;
        }
    }
}
