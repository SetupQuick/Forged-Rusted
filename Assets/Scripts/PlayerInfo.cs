using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Assemblies;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Unity.Properties;

public class PlayerInfo : MonoBehaviour
{
    [SerializeField] UIDocument UIDocument;
    private VisualElement root;
    private PlayerData playerData;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
       
        root = UIDocument.rootVisualElement;
        playerData = new PlayerData();
        root.dataSource = playerData;
       

        playerData.CurrentHealth -= 50;
        playerData.CurrentMana -= 10;
    }

    // Update is called once per frame
    void Update()
    {

    }

    public class PlayerData :INotifyPropertyChanged
    {
        private float maxHealth = 100f;
        public int _currentHealth = 100;

        public event PropertyChangedEventHandler PropertyChanged;

        [CreateProperty]
        public int CurrentHealth
        {

            get => _currentHealth;
            set
            {
                if(_currentHealth != value)
                {
                    _currentHealth = Mathf.Clamp(value, 0, (int)maxHealth);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HealthPercentage));

                }




            }
        }
        [CreateProperty]
        public Length HealthPercentage => new Length ((_currentHealth / maxHealth) * 100f, LengthUnit.Percent);

        private float maxMana = 50f;
        private int _currentMana = 50;

        [CreateProperty]
        public int CurrentMana
        {
            get => _currentMana;
            set
            {
                if (_currentMana != value)
                {
                    _currentMana = Mathf.Clamp(value, 0, (int)maxMana);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ManaPercentage));
                }
            }
        }
        [CreateProperty]
        public Length ManaPercentage => new Length((_currentMana / maxMana) * 100f, LengthUnit.Percent);







        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }

    
}
