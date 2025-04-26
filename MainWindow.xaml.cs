using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics; // <--- 添加 using for Debug
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls; // 明确引用 WPF Controls
using System.Windows.Controls.Primitives; // ToggleButton, ListViewItem
using System.Windows.Data;
using System.Windows.Documents; // Adorner
using System.Windows.Input;    // 明确引用 WPF Input
using System.Windows.Media;     // VisualTreeHelper, HitTestResult
using System.Windows.Forms; // <-- 保留，用于 NotifyIcon
using System.Drawing;      // <-- 保留，用于 Icon
using MessageBox = System.Windows.MessageBox; // 别名
using Application = System.Windows.Application; // 别名
using Path = System.IO.Path;               // 别名
// 不再使用 using System.Windows.Point 等可能冲突的单独 using

namespace DesktopToDo
{
    // --- 数据模型 ---
    public class ToDoItem : INotifyPropertyChanged // 实现接口以通知UI更新
    {
        private string _text = string.Empty;
        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value; OnPropertyChanged(nameof(Text)); } }
        }

        private bool _isDone;
        public bool IsDone
        {
            get => _isDone;
            set { if (_isDone != value) { _isDone = value; OnPropertyChanged(nameof(IsDone)); } }
        }

        private bool _isImportant;
        public bool IsImportant
        {
            get => _isImportant;
            set { if (_isImportant != value) { _isImportant = value; OnPropertyChanged(nameof(IsImportant)); } }
        }

        private string? _details; // 详细说明，可为空
        public string? Details
        {
            get => _details;
            set { if (_details != value) { _details = value; OnPropertyChanged(nameof(Details)); } }
        }


        // INotifyPropertyChanged 实现
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class AppSettings
    {
        public double WindowOpacity { get; set; } = 1.0;
        public List<ToDoItem> Tasks { get; set; } = new List<ToDoItem>(); // 保存时包含 Details
        public bool IsLeftPanelVisible { get; set; } = true;
        // public double WindowLeft { get; set; } = 10; // 未来可添加
        // public double WindowTop { get; set; } = 10; // 未来可添加
    }

    // --- 值转换器 (假设 NegateBooleanConverter 和 MultiBooleanToVisibilityConverter 在单独文件中) ---


    // --- 主窗口类 ---
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // --- 字段 ---
        private ObservableCollection<ToDoItem> _masterToDoItems = new ObservableCollection<ToDoItem>();
        private AppSettings _settings = new AppSettings();
        private string _dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopToDo", "data.json");
        private NotifyIcon? _notifyIcon;
        private CollectionViewSource? _toDoViewSource;
        private CollectionViewSource? _completedViewSource;
        private CollectionViewSource? _importantViewSource;
        private bool _isLeftPanelVisible = true;
        private bool _isDetailsViewVisible = false; // 跟踪是列表视图还是详情视图
        private System.Windows.Point _dragStartPoint; // 使用 System.Windows.Point
        private bool _isDragging = false; // 跟踪拖动状态
        private ToDoItem? _draggedItem = null; // 跟踪被拖动的项

        // --- 属性 ---
        public ObservableCollection<ToDoItem> ToDoItems => _masterToDoItems;

        private double _opacityValue = 1.0;
        public double OpacityValue
        {
            get => _opacityValue;
            set { _opacityValue = Math.Max(0.2, Math.Min(1.0, value)); OnPropertyChanged(nameof(OpacityValue)); }
        }

        private ToDoItem? _selectedToDoItem; // 当前选中的任务项，用于详情视图绑定
        public ToDoItem? SelectedToDoItem
        {
            get => _selectedToDoItem;
            set { if (_selectedToDoItem != value) { _selectedToDoItem = value; OnPropertyChanged(nameof(SelectedToDoItem)); } }
        }

        // --- INotifyPropertyChanged 实现 ---
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // --- 构造函数 ---
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this; // 设置数据上下文为窗口本身

            LoadData(); // 加载数据

            // 获取所有 CollectionViewSource 引用
            _toDoViewSource = (CollectionViewSource)this.Resources["ToDoViewSource"];
            _completedViewSource = (CollectionViewSource)this.Resources["CompletedViewSource"];
            _importantViewSource = (CollectionViewSource)this.Resources["ImportantViewSource"];

            InitializeNotifyIcon(); // 初始化托盘图标

            _isLeftPanelVisible = _settings.IsLeftPanelVisible; // 应用保存的侧边栏状态
            UpdateLeftPanelVisibility(false); // 应用初始状态

            // SetWindowPosition(); // <-- 调用移到 Loaded 事件

            ToDoRadioButton.IsChecked = true; // 默认选中“待完成”
            UpdateListViewSource(); // 初始化列表视图

            // 初始时显示列表视图，隐藏详情视图
            UpdateMainContentView(false);

            this.OpacityValue = _settings.WindowOpacity; // 设置初始透明度

            // 注册 Loaded 事件，在窗口加载完成后设置位置
            this.Loaded += MainWindow_Loaded;
            // *** 新增：在初始化后尝试显示图标 (如果图标加载成功) ***
            if (_notifyIcon != null && _notifyIcon.Icon != null)
            {
                try
                {
                    _notifyIcon.Visible = true;
                    Debug.WriteLine("[DEBUG] Constructor - Setting NotifyIcon.Visible = true.");
                }
                catch (Exception exVis)
                {
                    Debug.WriteLine($"[DEBUG] Constructor - Error setting NotifyIcon.Visible = true: {exVis.Message}");
#if DEBUG
                    MessageBox.Show($"在程序启动时设置托盘图标可见出错: {exVis.Message}", "托盘显示错误 (DEBUG)", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                }
            }
            else if (_notifyIcon != null) // 图标对象存在但图标本身是 null
            {
                Debug.WriteLine("[DEBUG] Constructor - NotifyIcon exists but Icon is null. Not setting Visible=true.");
            }
        }

        // 窗口 Loaded 事件处理
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetWindowPosition(); // 在窗口加载完成后调用，此时 ActualWidth/Height 可用
        }


        // --- 初始化方法 (添加了调试信息) ---
        private void InitializeNotifyIcon()
        {
            try // 将 new NotifyIcon() 也放入 try 块更安全
            {
                _notifyIcon = new NotifyIcon();
                _notifyIcon.Text = "桌面待办事项";
                Debug.WriteLine("[DEBUG] NotifyIcon object created.");

                var iconUri = new Uri("pack://application:,,,/todo_icon.ico");
                Debug.WriteLine($"[DEBUG] Attempting to load icon from: {iconUri}");
                var iconStreamResourceInfo = Application.GetResourceStream(iconUri);

                if (iconStreamResourceInfo != null && iconStreamResourceInfo.Stream != null)
                {
                    // 使用 using 确保 stream 被正确释放
                    using (var iconStream = iconStreamResourceInfo.Stream)
                    {
                        _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
                        Debug.WriteLine("[DEBUG] Icon loaded successfully from stream.");
                    }
                }
                else
                {
                    Debug.WriteLine($"[DEBUG] Error: Could not find or load resource stream for {iconUri}. GetResourceStream returned null or its Stream property was null. Ensure the file exists and Build Action is 'Resource'.");
                    _notifyIcon.Icon = null; // 明确设置为 null
#if DEBUG
                    MessageBox.Show($"未能加载图标资源: {iconUri}\n请检查:\n1. 图标文件是否存在于项目根目录。\n2. 图标文件的“生成操作”是否设置为“Resource”。\n\n托盘图标将不可见。", "图标错误 (DEBUG)", MessageBoxButton.OK, MessageBoxImage.Warning);
#endif
                }

                // 再次检查 Icon 是否真的被设置了
                if (_notifyIcon.Icon == null)
                {
                    Debug.WriteLine("[DEBUG] Warning: _notifyIcon.Icon is null after attempting to load. The tray icon may not be visible.");
#if DEBUG
                    MessageBox.Show("未能设置图标 (_notifyIcon.Icon is null)。\n可能是图标文件格式无效、损坏，或资源加载失败。\n\n托盘图标将不可见。", "图标错误 (DEBUG)", MessageBoxButton.OK, MessageBoxImage.Warning);
#endif
                }

            }
            catch (ArgumentException argEx) // 特别捕捉 ArgumentException，通常是无效图标文件
            {
                Debug.WriteLine($"[DEBUG] Error loading icon (ArgumentException): {argEx.Message}. Is todo_icon.ico a valid .ico file?");
                _notifyIcon.Icon = null;
#if DEBUG
                MessageBox.Show($"加载图标时发生参数错误 (可能图标格式无效): {argEx.Message}\n\n托盘图标将不可见。", "图标异常 (DEBUG)", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                // 确保即使出错，_notifyIcon 也不是 null (如果 new 成功的话)
                if (_notifyIcon == null) _notifyIcon = new NotifyIcon();
            }
            catch (Exception ex) // 捕捉其他所有异常
            {
                Debug.WriteLine($"[DEBUG] Error loading icon (General Exception): {ex.ToString()}"); // 使用 ToString() 获取更详细信息
                _notifyIcon.Icon = null;
#if DEBUG
                MessageBox.Show($"加载图标时发生未预料的异常: {ex.Message}\n\n请查看输出窗口获取详细信息。\n托盘图标将不可见。", "图标异常 (DEBUG)", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                // 确保即使出错，_notifyIcon 也不是 null (如果 new 成功的话)
                if (_notifyIcon == null) _notifyIcon = new NotifyIcon();
            }


            // 确保 _notifyIcon 对象本身不是 null
            if (_notifyIcon != null)
            {
                // 即使没有图标，也尝试设置 Visible 和 Click (虽然可能无效)
                // 这样如果问题在于 Visible 逻辑，还能继续调试
                _notifyIcon.Visible = false; // 初始隐藏
                _notifyIcon.Click += NotifyIcon_Click; // 注册点击事件
                Debug.WriteLine("[DEBUG] NotifyIcon initialized. Visible=false, Click event attached.");
            }
            else
            {
                // 这几乎不可能发生，除非 new NotifyIcon() 失败且没抛异常
                Debug.WriteLine("[DEBUG] Critical Error: _notifyIcon object itself is null after initialization attempt!");
#if DEBUG
                MessageBox.Show("严重错误: NotifyIcon 对象未能初始化！", "初始化错误 (DEBUG)", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
            }
        }


        // --- 窗口行为与系统托盘交互 (添加了调试日志)(修改：移除最小化时设置 Visible=true)  ---
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Debug.WriteLine("[DEBUG] OnStateChanged - WindowState is Minimized."); // 添加调试日志
                this.Hide();
                Debug.WriteLine("[DEBUG] OnStateChanged - Window hidden. Checking NotifyIcon...");
                // *** 不再需要在这里设置 Visible = true，因为它应该一直为 true (或者由构造函数设置) ***
            }
            // 当窗口从最小化恢复时 (例如通过任务栏或 Alt+Tab)，或通过 ShowWindowFromTray 恢复时,
            // 图标的 Visible 状态保持不变（应该一直为 true）
            else
            {
                Debug.WriteLine($"[DEBUG] OnStateChanged - WindowState is {WindowState}. Icon visibility remains unchanged.");
            }
                base.OnStateChanged(e);
            }

                /*if (_notifyIcon != null)
                {
                    Debug.WriteLine("[DEBUG] OnStateChanged - _notifyIcon object is not null.");
                    if (_notifyIcon.Icon != null)
                    {
                        Debug.WriteLine("[DEBUG] OnStateChanged - _notifyIcon.Icon is not null. Attempting to set Visible = true.");
                        try
                        {
                            _notifyIcon.Visible = true; // 尝试显示图标
                            Debug.WriteLine("[DEBUG] OnStateChanged - Successfully set NotifyIcon.Visible = true.");
                        }
                        catch (Exception exVisible) // 捕获设置 Visible 可能出现的异常
                        {
                            Debug.WriteLine($"[DEBUG] OnStateChanged - Error setting NotifyIcon.Visible = true: {exVisible.Message}");
#if DEBUG
                            MessageBox.Show($"设置托盘图标可见时出错: {exVisible.Message}", "托盘显示错误 (DEBUG)", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[DEBUG] OnStateChanged - Error: Cannot show tray icon because NotifyIcon.Icon is null.");
#if DEBUG
                        // 可能在 InitializeNotifyIcon 中已经提示过了，这里不再重复弹窗，只记录日志
#endif
                    }
                }
                else
                {
                    Debug.WriteLine("[DEBUG] OnStateChanged - Error: Cannot show tray icon because NotifyIcon object is null.");
#if DEBUG
                    // 可能在 InitializeNotifyIcon 中已经提示过了
#endif
                }
            }
            else // 如果窗口恢复正常或最大化
            {
                Debug.WriteLine($"[DEBUG] OnStateChanged - WindowState is {WindowState}.");
                // 确保托盘图标在窗口恢复时隐藏 (如果之前是可见的)
                if (_notifyIcon != null && _notifyIcon.Visible)
                {
                    Debug.WriteLine("[DEBUG] OnStateChanged - Hiding NotifyIcon because window is no longer minimized.");
                    _notifyIcon.Visible = false;
                }
            }
            base.OnStateChanged(e);
        }*/
        protected override void OnClosing(CancelEventArgs e)
        {
            _settings.IsLeftPanelVisible = _isLeftPanelVisible;
            _settings.WindowOpacity = this.OpacityValue;
            SaveData(); // 保存数据
            if (_notifyIcon != null)
            {
                Debug.WriteLine("[DEBUG] OnClosing - Disposing NotifyIcon.");
                _notifyIcon.Visible = false; // 先隐藏再 Dispose
                _notifyIcon.Dispose();
                _notifyIcon = null;
            } // 清理图标
            base.OnClosing(e);
        }
        private void NotifyIcon_Click(object? sender, EventArgs e)
        {
            Debug.WriteLine("[DEBUG] NotifyIcon_Click called.");
            ShowWindowFromTray();
        }
        /*private void ShowWindowFromTray()
        {
            Debug.WriteLine("[DEBUG] ShowWindowFromTray called.");
            this.Show(); this.WindowState = WindowState.Normal; this.Activate();
            if (_notifyIcon != null)
            {
                Debug.WriteLine("[DEBUG] ShowWindowFromTray - Hiding NotifyIcon.");
                _notifyIcon.Visible = false;
            }
        }*/
        // --- 修改：ShowWindowFromTray 不再隐藏图标，并处理已显示窗口的激活 ---
        private void ShowWindowFromTray()
        {
            Debug.WriteLine("[DEBUG] ShowWindowFromTray called.");
            // 如果窗口当前不可见（隐藏状态），则显示并恢复正常状态
            if (!this.IsVisible)
            {
                Debug.WriteLine("[DEBUG] ShowWindowFromTray - Window is not visible. Showing and restoring.");
                this.Show();
                this.WindowState = WindowState.Normal;
            }
            // 无论窗口之前是否可见，都将其激活（带到前台）
            Debug.WriteLine("[DEBUG] ShowWindowFromTray - Activating window.");
            bool activated = this.Activate(); // Activate() 返回是否成功
            Debug.WriteLine($"[DEBUG] ShowWindowFromTray - Activation result: {activated}");
            // 确保窗口在最前 (有时 Activate 不够)
            this.Topmost = true;
            this.Topmost = false; // 设置两次 Topmost 是一个常用的技巧，确保窗口到最前
            this.Focus(); // 尝试设置焦点

            // *** 不再隐藏图标 ***
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseApplication();
        private void CloseApplication() => this.Close();
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) // 使用 System.Windows.Input.MouseButtonEventArgs
        {
            // 只在非控件区域（例如顶部空白区域）按下时拖动
            if (e.OriginalSource == sender || e.OriginalSource is Border || (e.OriginalSource is Grid && ((Grid)e.OriginalSource).Name != "DetailsViewGroup" && ((Grid)e.OriginalSource).Name != "ListViewGroup"))
            {
                if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
            }
        }
        // 设置窗口初始位置（屏幕中央）
        private void SetWindowPosition()
        {
            // 确保在 ActualWidth/Height 有效时调用 (通过 Loaded 事件)
            if (this.ActualWidth > 0 && this.ActualHeight > 0)
            {
                var screenWidth = SystemParameters.WorkArea.Width;
                var screenHeight = SystemParameters.WorkArea.Height;
                this.Left = (screenWidth - this.ActualWidth) / 2;
                this.Top = (screenHeight - this.ActualHeight) / 2;
                Debug.WriteLine($"[DEBUG] SetWindowPosition called. Left={this.Left}, Top={this.Top}, ActualWidth={this.ActualWidth}, ActualHeight={this.ActualHeight}");
            }
            else
            {
                Debug.WriteLine("[DEBUG] SetWindowPosition called but ActualWidth/Height are zero. Position not set.");
                // 可以考虑设置一个默认值或稍后重试
            }
        }

        // --- 分组和过滤 ---
        public void ToDoFilter(object sender, FilterEventArgs e) { if (e.Item is ToDoItem item) e.Accepted = !item.IsDone; else e.Accepted = false; }
        public void CompletedFilter(object sender, FilterEventArgs e) { if (e.Item is ToDoItem item) e.Accepted = item.IsDone; else e.Accepted = false; }
        public void ImportantFilter(object sender, FilterEventArgs e) { if (e.Item is ToDoItem item) e.Accepted = item.IsImportant; else e.Accepted = false; }
        private void GroupSelector_Checked(object sender, RoutedEventArgs e) => UpdateListViewSource();
        // 更新列表数据源，并在切换时确保返回列表视图
        private void UpdateListViewSource()
        {
            if (_isDetailsViewVisible) { UpdateMainContentView(false); SelectedToDoItem = null; ToDoListView.SelectedItem = null; } // 切换时关闭详情
            if (_toDoViewSource == null || _completedViewSource == null || _importantViewSource == null || ToDoListView == null) return;
            if (ToDoRadioButton.IsChecked == true) ToDoListView.ItemsSource = _toDoViewSource.View;
            else if (CompletedRadioButton.IsChecked == true) ToDoListView.ItemsSource = _completedViewSource.View;
            else if (ImportantRadioButton.IsChecked == true) ToDoListView.ItemsSource = _importantViewSource.View;
            RefreshViews(); // 切换后刷新
        }

        // --- 任务管理逻辑 ---
        private void AddTaskButton_Click(object sender, RoutedEventArgs e) => AddTask();
        private void NewTaskTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) AddTask(); } // 使用 System.Windows.Input.KeyEventArgs
        private void AddTask()
        {
            string newTaskText = NewTaskTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(newTaskText))
            {
                var newItem = new ToDoItem { Text = newTaskText, IsDone = false, IsImportant = false, Details = null };
                _masterToDoItems.Add(newItem);
                NewTaskTextBox.Clear();
                UpdateListViewSource();
                //RefreshViews(); // 刷新所有视图源
                // 不需要手动刷新 ItemsSource 了，因为 ObservableCollection 会通知 CollectionViewSource
                SaveData();
            }
        }
        private void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button deleteButton && deleteButton.Tag is ToDoItem itemToDelete)
            {
                if (_isDetailsViewVisible && SelectedToDoItem == itemToDelete) { UpdateMainContentView(false); SelectedToDoItem = null; }
                _masterToDoItems.Remove(itemToDelete); SaveData();
            }
        }
        private void DoneCheckBox_Changed(object sender, RoutedEventArgs e) { RefreshViews(); SaveData(); }
        private void StarToggleButton_Click(object sender, RoutedEventArgs e) { RefreshViews(); SaveData(); }
        // 刷新所有 CollectionViewSource 视图
        private void RefreshViews()
        {
            _toDoViewSource?.View?.Refresh(); _completedViewSource?.View?.Refresh(); _importantViewSource?.View?.Refresh();
        }

        // --- 左侧面板可见性控制 ---
        private void ToggleLeftPanelButton_Click(object sender, RoutedEventArgs e)
        {
            _isLeftPanelVisible = !_isLeftPanelVisible; UpdateLeftPanelVisibility(true);
        }
        private void UpdateLeftPanelVisibility(bool isToggleAction)
        {
            if (_isDetailsViewVisible) return; // 详情视图时不允许切换
            if (_isLeftPanelVisible) { LeftPanel.Visibility = Visibility.Visible; LeftPanelColumn.Width = new GridLength(1, GridUnitType.Auto); SeparatorColumn.Width = new GridLength(15); }
            else { LeftPanel.Visibility = Visibility.Collapsed; LeftPanelColumn.Width = new GridLength(0); SeparatorColumn.Width = new GridLength(0); }
        }

        // --- 详情视图逻辑 ---
        private void ToDoListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) // 明确指定参数类型
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            var item = FindAncestor<System.Windows.Controls.ListViewItem>(source); // 使用 WPF ListViewItem
            if (item != null && item.DataContext is ToDoItem selectedItem)
            {
                this.SelectedToDoItem = selectedItem;
                DetailsTextBox.Text = selectedItem.Details ?? string.Empty;
                UpdateMainContentView(true);
                DetailsTextBox.Focus();
                e.Handled = true;
            }
        }
        private void SaveDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedToDoItem != null) { SelectedToDoItem.Details = DetailsTextBox.Text; SaveData(); }
            UpdateMainContentView(false); ToDoListView.SelectedItem = null; ToDoListView.Focus();
        }
        private void CancelDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateMainContentView(false); ToDoListView.SelectedItem = null; ToDoListView.Focus();
        }
        private void UpdateMainContentView(bool showDetails)
        {
            _isDetailsViewVisible = showDetails;
            if (ListViewGroup != null) ListViewGroup.Visibility = showDetails ? Visibility.Collapsed : Visibility.Visible;
            if (DetailsViewGroup != null) DetailsViewGroup.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
            if (ToggleLeftPanelButton != null) ToggleLeftPanelButton.IsEnabled = !showDetails;
            if (LeftPanel != null) LeftPanel.IsEnabled = !showDetails;
            if (OpacitySlider != null) OpacitySlider.IsEnabled = !showDetails;
            if (NewTaskTextBox != null) NewTaskTextBox.IsEnabled = !showDetails;
            if (AddTaskButton != null) AddTaskButton.IsEnabled = !showDetails;
        }

        // --- 拖放排序逻辑 ---
        private void ListViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) // 使用 System.Windows.Input.MouseButtonEventArgs
        {
            if (ToDoRadioButton.IsChecked == true && !_isDetailsViewVisible)
            {
                _dragStartPoint = e.GetPosition(null); // 使用 System.Windows.Point
                if (sender is System.Windows.Controls.ListViewItem item && item.DataContext is ToDoItem draggedItem) // 使用 System.Windows.Controls.ListViewItem
                { _draggedItem = draggedItem; _isDragging = false; }
                else { _draggedItem = null; }
            }
            else { _draggedItem = null; }
        }
        private void ListViewItem_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e) // 使用 System.Windows.Input.MouseEventArgs
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null && !_isDragging)
            {
                System.Windows.Point position = e.GetPosition(null); // 使用 System.Windows.Point
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    if (sender is System.Windows.Controls.ListViewItem listViewItem) // 使用 System.Windows.Controls.ListViewItem
                    { System.Windows.DragDrop.DoDragDrop(listViewItem, _draggedItem, System.Windows.DragDropEffects.Move); } // 使用 System.Windows.DragDrop 和 System.Windows.DragDropEffects
                    _draggedItem = null; _isDragging = false; // 拖放结束后清理
                }
            }
        }
        private void ToDoListView_DragEnter(object sender, System.Windows.DragEventArgs e) // 使用 System.Windows.DragEventArgs
        {
            if (ToDoRadioButton.IsChecked == true && e.Data.GetDataPresent(typeof(ToDoItem)))
            { e.Effects = System.Windows.DragDropEffects.Move; } // 使用 System.Windows.DragDropEffects
            else { e.Effects = System.Windows.DragDropEffects.None; } // 使用 System.Windows.DragDropEffects
            e.Handled = true;
        }
        private void ToDoListView_DragOver(object sender, System.Windows.DragEventArgs e) // 使用 System.Windows.DragEventArgs
        {
            if (ToDoRadioButton.IsChecked == true && e.Data.GetDataPresent(typeof(ToDoItem)))
            { e.Effects = System.Windows.DragDropEffects.Move; } // 使用 System.Windows.DragDropEffects
            else { e.Effects = System.Windows.DragDropEffects.None; } // 使用 System.Windows.DragDropEffects
            e.Handled = true;
        }
        private void ToDoListView_Drop(object sender, System.Windows.DragEventArgs e) // 使用 System.Windows.DragEventArgs
        {
            if (ToDoRadioButton.IsChecked == true && e.Data.GetDataPresent(typeof(ToDoItem)))
            {
                ToDoItem? droppedData = e.Data.GetData(typeof(ToDoItem)) as ToDoItem;
                ToDoItem? targetItem = null;
                System.Windows.Controls.ListViewItem? targetListViewItem = GetListViewItemFromPoint(ToDoListView, e.GetPosition(ToDoListView)); // 使用 System.Windows.Point 和 System.Windows.Controls.ListViewItem

                if (targetListViewItem != null) { targetItem = targetListViewItem.DataContext as ToDoItem; }

                if (droppedData != null && targetItem != null && !Equals(droppedData, targetItem))
                {
                    int originalIndex = _masterToDoItems.IndexOf(droppedData);
                    int targetIndex = _masterToDoItems.IndexOf(targetItem);
                    if (originalIndex != -1 && targetIndex != -1) { _masterToDoItems.Move(originalIndex, targetIndex); SaveData(); }
                }
                else if (droppedData != null && targetListViewItem == null) // 拖到末尾
                {
                    int originalIndex = _masterToDoItems.IndexOf(droppedData);
                    if (originalIndex != -1)
                    {
                        // 移动到当前可见的未完成项的末尾
                        var visibleItems = _masterToDoItems.Where(i => !i.IsDone).ToList();
                        if (visibleItems.Any())
                        {
                            int lastVisibleIndexInMaster = _masterToDoItems.IndexOf(visibleItems.Last());
                            if (originalIndex < lastVisibleIndexInMaster) { _masterToDoItems.Move(originalIndex, lastVisibleIndexInMaster); SaveData(); }
                        }
                    }
                }
            }
            _draggedItem = null; _isDragging = false; // 清理状态
            e.Handled = true;
        }

        // --- 辅助方法 ---
        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        { while (current != null) { if (current is T ancestor) { return ancestor; } current = VisualTreeHelper.GetParent(current); } return null; }
        private System.Windows.Controls.ListViewItem? GetListViewItemFromPoint(System.Windows.Controls.ListView listView, System.Windows.Point point) // 使用 System.Windows.Controls.ListView 和 System.Windows.Point
        {
            HitTestResult hitTestResult = VisualTreeHelper.HitTest(listView, point);
            if (hitTestResult != null)
            {
                DependencyObject? obj = hitTestResult.VisualHit;
                return FindAncestor<System.Windows.Controls.ListViewItem>(obj); // 使用 System.Windows.Controls.ListViewItem
            }
            return null;
        }

        // --- 数据持久化 ---
        private void LoadData()
        {
            string? directory = Path.GetDirectoryName(_dataPath);
            if (directory == null) { MessageBox.Show($"无法确定数据目录: {_dataPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            if (!Directory.Exists(directory)) { try { Directory.CreateDirectory(directory); } catch (Exception ex) { MessageBox.Show($"创建数据目录失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); return; } }
            if (File.Exists(_dataPath))
            {
                try
                {
                    string json = File.ReadAllText(_dataPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    _masterToDoItems.Clear();
                    foreach (var item in _settings.Tasks ?? new List<ToDoItem>())
                    {
                        _masterToDoItems.Add(new ToDoItem { Text = item.Text, IsDone = item.IsDone, IsImportant = item.IsImportant, Details = item.Details });
                    }
                }
                catch (JsonException jsonEx) { MessageBox.Show($"加载数据失败: JSON格式错误。\n{jsonEx.Message}\n将使用默认设置。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning); _settings = new AppSettings(); _masterToDoItems.Clear(); }
                catch (Exception ex) { MessageBox.Show($"加载数据失败: {ex.Message}\n将使用默认设置。", "错误", MessageBoxButton.OK, MessageBoxImage.Error); _settings = new AppSettings(); _masterToDoItems.Clear(); }
            }
            else { _settings = new AppSettings(); _masterToDoItems.Clear(); }
        }
        private void SaveData()
        {
            _settings.IsLeftPanelVisible = this._isLeftPanelVisible; _settings.WindowOpacity = this.OpacityValue; _settings.Tasks = _masterToDoItems.ToList();
            try
            {
                string? directory = Path.GetDirectoryName(_dataPath);
                if (directory != null && !Directory.Exists(directory)) { try { Directory.CreateDirectory(directory); } catch { /* 忽略 */ } }
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataPath, json);
            }
            catch (Exception ex) { MessageBox.Show($"保存数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}