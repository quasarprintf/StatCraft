using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using StatCraft.ViewModels;

namespace StatCraft.Views.Components
{
    public partial class ColoredTextBlock : UserControl
    {
        public static readonly StyledProperty<IEnumerable<ColoredCharacter>?> CharactersProperty =
            AvaloniaProperty.Register<ColoredTextBlock, IEnumerable<ColoredCharacter>?>(nameof(Characters));

        public IEnumerable<ColoredCharacter>? Characters
        {
            get => GetValue(CharactersProperty);
            set => SetValue(CharactersProperty, value);
        }

        public ColoredTextBlock()
        {
            InitializeComponent();
        }
    }
}
