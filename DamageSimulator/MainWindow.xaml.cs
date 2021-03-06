﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BindableWinFormsControl {
	/// <summary>
	/// MainWindow.xaml の相互作用ロジック
	/// </summary>
	public partial class MainWindow : Window {
		/* メンバ変数 */
		//変数
		bool autoCalcFlg = false;	//自動再計算フラグ
		Chart chart;				//グラフ
		SortedDictionary<int, int> sorted_hist; //ヒストグラム
		public static Dictionary<string, Dictionary<string, Dictionary<string, int[]>>> preset_data = null;
		//定数
		System.Random rand = new System.Random();	//乱数シード
		const int TabIndexGun     = 0;	//砲撃戦
		const int TabIndexGunAir  = 1;	//砲撃戦(空母)
		const int TabIndexTorpedo = 2;	//雷撃戦
		const int TabIndexAir     = 3;	//航空戦
		const int TabIndexAntiSub = 4;	//対潜攻撃
		const int TabIndexNight   = 5;	//夜戦
		int[] loopCount = { 1000, 10000, 100000, 1000000 };	//ループカウント
		enum StatusIndex {
			NoDamage, TooSmall, Small, Middle, Large, Destroyed,
			NormalMin, NormalMax, CriticalMin, CriticalMax,
			NormalMinBig, NormalMaxBig, CriticalMinBig, CriticalMaxBig,
			NormalPerDamage, CriticalPerDamage, NormalPerDamageBig, CriticalPerDamageBig,
		};
		enum PresetData {
			Attack, Torpedo1, Bomb, Torpedo2, PAPB_Power,
			SlotSize, AntiSubBody, AntiSubWeapon, Defense, HP,
			KammusuFlg,MarriageHP, MarriageAntiSubBody, Size,
		};

		/* コンストラクタ */
		public MainWindow() {
			InitializeComponent();
			// チャート用の初期設定
			var windowsFormsHost1 = (WindowsFormsHost)grid_Graph.Children[0];
			chart = (Chart)windowsFormsHost1.Child;
			chart.ChartAreas.Add("ChartArea");
			// BindableNumericUpDown用の初期設定
			DataContext = new TestBindObject() {
				AttackGun = 50,
				AttackGunAir = 40,
				BombGunAir = 10,
				TorpedoGunAir = 10,
				Torpedo = 80,
				PowerAir = 30,
				SlotsAir = 10,
				AntiSubKammusu = 50,
				AntiSubWeapons = 28,
				AttackNight = 70,
				TorpedoNight = 30,
				Defense = 50,
				MaxHP = 50,
				NowHP = 50,
				Critical = 133,
				StatusMessage = "",
				ShipTypeList = null,
				AmmoParam = 50,
			};
			// 右クリックメニュー用のデータを読み込む
			try {
				/* 配列に読み出す
				 * arr[行番号][列番号] X行14列
				 * 行番号→0は列名、1以降がデータ
				 * 列番号→0から順に番号,艦種,艦型,艦名,火力,雷装1,爆装,雷装2,
				 * 艦載機力(艦爆は正、艦攻は負),搭載数,素対潜,装備対潜,装甲,耐久,
				 * 艦娘フラグ
				 */
				var sr = new StreamReader("preset.csv", Encoding.GetEncoding("UTF-8"));
				var list = new List<string[]>();
				while(sr.Peek() >= 0)
					list.Add(sr.ReadLine().Split(','));
				sr.Close();
				var arr = list.ToArray();
				//! 多段連想配列に変換
				preset_data = new Dictionary<string, Dictionary<string, Dictionary<string, int[]>>>();
				for(int i = 1; i < arr.Length; ++i) {
					//! 必要な部分を抜き出す
					var type_str = arr[i][1];
					var class_str = arr[i][2];
					var name_str = arr[i][3];
					var parameter_str = arr[i].Skip(4).Take((int)PresetData.Size).ToArray();
					var parameter = new int[(int)PresetData.Size];
					//! 適宜拡張しつつ、代入していく
					if(!preset_data.ContainsKey(type_str))
						preset_data[type_str] = new Dictionary<string, Dictionary<string, int[]>> ();
					if(!preset_data[type_str].ContainsKey(class_str))
						preset_data[type_str][class_str] = new Dictionary<string, int[]> ();
					if(!preset_data[type_str][class_str].ContainsKey(name_str))
						preset_data[type_str][class_str][name_str] = new int[(int)PresetData.Size];
					for(int j = 0; j < (int)PresetData.Size; ++j) {
						preset_data[type_str][class_str][name_str][j] = int.Parse(parameter_str[j]);
					}
				}
				//! メニューを動的に作成
				var bindData = DataContext as TestBindObject;
				bindData.ShipTypeList = new List<string>(preset_data.Keys);
			} catch {
				System.Windows.Forms.MessageBox.Show("プリセットファイルを読み込めませんでした。", "KancolleDamageSimulator", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		/* ヒストグラム関係 */
		/// <summary>
		/// ヒストグラムの計算処理
		/// </summary>
		private void DrawHistogram() {
			try {
				// 計算処理
				CalcData();
				// ヒストグラムを描画させる
				DrawGraph();
			} catch { }
		}
		/// <summary>
		/// グラフを再描画する処理
		/// </summary>
		private void DrawGraph() {
			// スケールの最大・最小値を計算する
			double axisX_Max = double.MinValue, axisX_Min = double.MaxValue, axisY_Max = double.MinValue, axisY_Min = double.MaxValue;
			foreach(var pair in sorted_hist) {
				var valuePer = 100.0 * pair.Value / loopCount[comboBox_TryCount.SelectedIndex];
				axisX_Max = Math.Max(axisX_Max, pair.Key);
				axisX_Min = Math.Min(axisX_Min, pair.Key);
				axisY_Max = Math.Max(axisY_Max, valuePer);
				axisY_Min = Math.Min(axisY_Min, valuePer);
			}
			axisX_Min = Math.Floor(axisX_Min);
			axisX_Max = Math.Ceiling(axisX_Max);
			axisY_Min = Math.Floor(axisY_Min);
			axisY_Max = Math.Ceiling(axisY_Max);
			// チャートにグラフを追加する
			chart.Series.Clear();
			var seriesHist = new Series();
			seriesHist.ChartType = SeriesChartType.Line;
			seriesHist.MarkerStyle = MarkerStyle.Circle;
			foreach(var pair in sorted_hist) {
				// 値を取得しつつ、スケールの最大・最小値を計算する
				var valuePer = 100.0 * pair.Value / loopCount[comboBox_TryCount.SelectedIndex];
				seriesHist.Points.AddXY(pair.Key, valuePer);
			}
			chart.Series.Add(seriesHist);
			chart.ChartAreas[0].AxisX.Minimum = axisX_Min;
			chart.ChartAreas[0].AxisX.Maximum = axisX_Max;
			chart.ChartAreas[0].AxisY.Minimum = 0.0;
			chart.ChartAreas[0].AxisY.Maximum = axisY_Max;
		}
		/// <summary>
		/// ヒストグラムの文字列を作成する
		/// </summary>
		private string MakeHistText() {
			var count = 0;
			foreach(var pair in sorted_hist) {
				count += pair.Value;
			}
			// ダメージ,カウント,確率,標本標準偏差,95％信頼区間(最小・最大)
			string histText = "damage,count,prob,SD,CI(95%)-min,CI(95%)-max\n";
			foreach(var pair in sorted_hist) {
				var average = 1.0 * pair.Value / count;
				var SD = Math.Sqrt(((1 - average) * (1 - average) * pair.Value + (0 - average) * (0 - average) * (count - pair.Value)) / (count - 1));
				var CI = 1.96 * SD / Math.Sqrt(count);
				histText += "" + pair.Key + "," + pair.Value + "," + average + "," + SD + "," + (average - CI) + ","  +(average + CI) + "\n";
			}
			return histText;
		}

		/* クリック時の動作 */
		/// <summary>
		/// スライドバーを動かした際の処理
		/// </summary>
		private void slider_Critical_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
			AutoDrawHistogram();
		}
		/// <summary>
		/// 「計算開始」ボタンを押した際の処理
		/// </summary>
		private void button_Run_Click(object sender, RoutedEventArgs e) {
			DrawHistogram();
		}
		/// <summary>
		/// 「自動再計算」チェックを付けた際の処理
		/// </summary>
		private void checkBox_AutoCalc_Checked(object sender, RoutedEventArgs e) {
			autoCalcFlg = true;
			AutoDrawHistogram();
		}
		/// <summary>
		/// 「自動再計算」チェックを外した際の処理
		/// </summary>
		private void checkBox_AutoCalc_Unchecked(object sender, RoutedEventArgs e) {
			autoCalcFlg = false;
		}
		/// <summary>
		/// 最大耐久を変化させた際の処理
		/// </summary>
		private void NUD_MaxHP_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			var bindData = DataContext as TestBindObject;
			if(bindData.NowHP > bindData.MaxHP)
				bindData.NowHP = bindData.MaxHP;
			AutoDrawHistogram();
		}
		/// <summary>
		/// 現在耐久を変化させた際の処理
		/// </summary>
		private void NUD_NowHP_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			var bindData = DataContext as TestBindObject;
			if(bindData.NowHP > bindData.MaxHP)
				bindData.MaxHP = bindData.NowHP;
			AutoDrawHistogram();
		}
		/// <summary>
		/// タブを切り替えた際の処理
		/// </summary>
		private void tabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			switch(tabControl.SelectedIndex) {
			case TabIndexAir:
				comboBox_FleetOption.IsEnabled = false;
				comboBox_Position.IsEnabled = false;
				comboBox_Formation.IsEnabled = false;
				comboBox_Damage.IsEnabled = false;
				break;
			case TabIndexNight:
				comboBox_FleetOption.IsEnabled = false;
				comboBox_Position.IsEnabled = false;
				comboBox_Formation.IsEnabled = false;
				comboBox_Damage.IsEnabled = true;
				break;
			default:
				comboBox_FleetOption.IsEnabled = true;
				comboBox_Position.IsEnabled = true;
				comboBox_Formation.IsEnabled = true;
				comboBox_Damage.IsEnabled = true;
				break;
			}
			AutoDrawHistogram();
		}
		/// <summary>
		/// 陣形を変化させた際の処理
		/// </summary>
		private void comboBox_Formation_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if(comboBox_FleetOption == null)
				return;
			var a = comboBox_FleetOption.SelectedIndex;
			//! 通常艦隊モードでは、一部の陣形は選択できない
			if((comboBox_FleetOption.SelectedIndex == 0) && (comboBox_Formation.SelectedIndex >= 5)) {
				//! 直感に反しないよう、それぞれ対応した陣形に変換する
				switch(comboBox_Formation.SelectedIndex) {
				case 5:
					//! 第一警戒航行序列(対潜警戒)→単横陣
					comboBox_Formation.SelectedIndex = 4;
					break;
				case 6:
					//! 第二警戒航行序列(前方警戒)→複縦陣
					comboBox_Formation.SelectedIndex = 1;
					break;
				case 7:
					//! 第三警戒航行序列(輪形陣)→輪形陣
					comboBox_Formation.SelectedIndex = 2;
					break;
				case 8:
					//! 第四警戒航行序列(戦闘隊形)→単縦陣
					comboBox_Formation.SelectedIndex = 0;
					break;
				default:
					break;
				}
			}
			AutoDrawHistogram();
		}
		/// <summary>
		/// 敵艦の形態を変化させた際の処理
		/// </summary>
		private void comboBox_Enemy_Type_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if(comboBox_Enemy_Type == null
			|| comboBox_Shell == null
			|| checkBox_Sanshiki == null
			|| comboBox_WG42 == null
			|| comboBox_Landing_Craft == null
			|| comboBox_KaMi == null
			|| checkBox_WBWF == null
			|| checkBox_DDCL == null)
				return;
			switch(comboBox_Enemy_Type.SelectedIndex) {
			case 0:
				// 通常艦：徹甲弾特殊効果が効くこともある
				comboBox_Shell.IsEnabled = true;
				checkBox_Sanshiki.IsEnabled = false;
				comboBox_WG42.IsEnabled = false;
				comboBox_Landing_Craft.IsEnabled = false;
				comboBox_KaMi.IsEnabled = false;
				checkBox_WBWF.IsEnabled = false;
				checkBox_DDCL.IsEnabled = false;
				break;
			case 1:
				// 通常陸上型：徹甲弾・三式弾・WG42(キャップ前加算)が効く
				comboBox_Shell.IsEnabled = true;
				checkBox_Sanshiki.IsEnabled = true;
				comboBox_WG42.IsEnabled = true;
				comboBox_Landing_Craft.IsEnabled = false;
				comboBox_KaMi.IsEnabled = false;
				checkBox_WBWF.IsEnabled = false;
				checkBox_DDCL.IsEnabled = false;
				break;
			case 2:
				// 集積地棲姫：徹甲弾・三式弾・WG42・大発系・カミ車(キャップ前加算・キャップ後乗算)が効く
				comboBox_Shell.IsEnabled = true;
				checkBox_Sanshiki.IsEnabled = true;
				comboBox_WG42.IsEnabled = true;
				comboBox_Landing_Craft.IsEnabled = true;
				comboBox_KaMi.IsEnabled = true;
				checkBox_WBWF.IsEnabled = false;
				checkBox_DDCL.IsEnabled = false;
				break;
			case 3:
				// 砲台子鬼：徹甲弾・WG42(キャップ前乗算加算)・大発系・カミ車・水爆水戦が効く
				comboBox_Shell.IsEnabled = true;
				checkBox_Sanshiki.IsEnabled = false;
				comboBox_WG42.IsEnabled = true;
				comboBox_Landing_Craft.IsEnabled = true;
				comboBox_KaMi.IsEnabled = true;
				checkBox_WBWF.IsEnabled = true;
				checkBox_DDCL.IsEnabled = true;
				break;
			case 4:
				// 離島棲姫：三式弾(乗算倍率が違う)・WG42(キャップ前乗算加算)が効く
				comboBox_Shell.IsEnabled = false;
				checkBox_Sanshiki.IsEnabled = true;
				comboBox_WG42.IsEnabled = true;
				comboBox_Landing_Craft.IsEnabled = false;
				comboBox_KaMi.IsEnabled = false;
				checkBox_WBWF.IsEnabled = false;
				checkBox_DDCL.IsEnabled = false;
				break;
			}
			AutoDrawHistogram();
		}
		private void comboBox_Enemy_Type_Night_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if(comboBox_Enemy_Type_Night == null
	|| checkBox_Shiell_Night == null
	|| checkBox_Sanshiki_Night == null
	|| comboBox_WG42_Night == null
	|| comboBox_Landing_Craft_Night == null
	|| comboBox_KaMi_Night == null
	|| checkBox_WBWF_Night == null
	|| checkBox_DDCL_Night == null)
				return;
			switch(comboBox_Enemy_Type_Night.SelectedIndex) {
			case 0:
				// 通常艦：夜戦なので徹甲弾特殊効果がない
				checkBox_Shiell_Night.IsEnabled = false;
				checkBox_Sanshiki_Night.IsEnabled = false;
				comboBox_WG42_Night.IsEnabled = false;
				comboBox_Landing_Craft_Night.IsEnabled = false;
				comboBox_KaMi_Night.IsEnabled = false;
				checkBox_WBWF_Night.IsEnabled = false;
				checkBox_DDCL_Night.IsEnabled = false;
				break;
			case 1:
				// 通常陸上型：三式弾・WG42(キャップ前加算)が効く
				checkBox_Shiell_Night.IsEnabled = false;
				checkBox_Sanshiki_Night.IsEnabled = true;
				comboBox_WG42_Night.IsEnabled = true;
				comboBox_Landing_Craft_Night.IsEnabled = false;
				comboBox_KaMi_Night.IsEnabled = false;
				checkBox_WBWF_Night.IsEnabled = false;
				checkBox_DDCL_Night.IsEnabled = false;
				break;
			case 2:
				// 集積地棲姫：三式弾・WG42・大発系・カミ車(キャップ前加算・キャップ後乗算)が効く
				checkBox_Shiell_Night.IsEnabled = false;
				checkBox_Sanshiki_Night.IsEnabled = true;
				comboBox_WG42_Night.IsEnabled = true;
				comboBox_Landing_Craft_Night.IsEnabled = true;
				comboBox_KaMi_Night.IsEnabled = true;
				checkBox_WBWF_Night.IsEnabled = false;
				checkBox_DDCL_Night.IsEnabled = false;
				break;
			case 3:
				// 砲台子鬼：徹甲弾・WG42(キャップ前乗算加算)・大発系・カミ車・水爆水戦が効く
				checkBox_Shiell_Night.IsEnabled = true;
				checkBox_Sanshiki_Night.IsEnabled = false;
				comboBox_WG42_Night.IsEnabled = true;
				comboBox_Landing_Craft_Night.IsEnabled = true;
				comboBox_KaMi_Night.IsEnabled = true;
				checkBox_WBWF_Night.IsEnabled = true;
				checkBox_DDCL_Night.IsEnabled = true;
				break;
			case 4:
				// 離島棲姫：三式弾(乗算倍率が違う)・WG42(キャップ前乗算加算)が効く
				checkBox_Shiell_Night.IsEnabled = false;
				checkBox_Sanshiki_Night.IsEnabled = true;
				comboBox_WG42_Night.IsEnabled = true;
				comboBox_Landing_Craft_Night.IsEnabled = false;
				comboBox_KaMi_Night.IsEnabled = false;
				checkBox_WBWF_Night.IsEnabled = false;
				checkBox_DDCL_Night.IsEnabled = false;
				break;
			}
			AutoDrawHistogram();
		}
		/// <summary>
		/// 大発数やカミ車数を変化させた際の処理
		/// </summary>
		private void comboBox_Landing_Craft_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			comboBox_Attack_Gun_Type_0.SelectedIndex = (comboBox_Landing_Craft.SelectedIndex != 0 ? 3 : 0);
			comboBox_Attack_Gun_Type_1.SelectedIndex = (comboBox_Landing_Craft.SelectedIndex == 3 ? 3 : 0);
		}
		private void comboBox_KaMi_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			comboBox_Attack_Gun_Type_2.SelectedIndex = (comboBox_KaMi.SelectedIndex != 0 ? 4 : 0);
			comboBox_Attack_Gun_Type_3.SelectedIndex = (comboBox_KaMi.SelectedIndex == 2 ? 4 : 0);
		}
		private void comboBox_Landing_Craft_Night_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if(comboBox_Attack_Gun_Type_0_Night == null || comboBox_Attack_Gun_Type_1_Night == null)
				return;
			comboBox_Attack_Gun_Type_0_Night.SelectedIndex = (comboBox_Landing_Craft_Night.SelectedIndex != 0 ? 1 : 0);
			comboBox_Attack_Gun_Type_1_Night.SelectedIndex = (comboBox_Landing_Craft_Night.SelectedIndex == 3 ? 1 : 0);
		}
		private void comboBox_KaMi_Night_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if(comboBox_Attack_Gun_Type_2_Night == null || comboBox_Attack_Gun_Type_3_Night == null)
				return;
			comboBox_Attack_Gun_Type_2_Night.SelectedIndex = (comboBox_KaMi_Night.SelectedIndex != 0 ? 2 : 0);
			comboBox_Attack_Gun_Type_3_Night.SelectedIndex = (comboBox_KaMi_Night.SelectedIndex == 2 ? 2 : 0);
		}
		/// <summary>
		/// プリセット設定を変化させた際の処理
		/// </summary>
		private void comboBox_ShipType_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			var bindData = DataContext as TestBindObject;
			bindData.ShipClassList = new List<string>(preset_data[(string)comboBox_ShipType.SelectedItem].Keys);
			comboBox_ShipClass.IsEnabled = true;
			comboBox_ShipClass.SelectedIndex = -1;
			comboBox_ShipName.IsEnabled = false;
			comboBox_ShipName.SelectedIndex = -1;
		}
		private void comboBox_ShipClass_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if(comboBox_ShipClass.SelectedItem == null)
				return;
			var bindData = DataContext as TestBindObject;
			bindData.ShipNameList = new List<string>(preset_data[(string)comboBox_ShipType.SelectedItem][(string)comboBox_ShipClass.SelectedItem].Keys);
			comboBox_ShipName.IsEnabled = true;
			comboBox_ShipName.SelectedIndex = -1;
		}
		/// <summary>
		/// プリセットをGUIに反映させる処理
		/// </summary>
		private void button_SetShipA_Click(object sender, RoutedEventArgs e) {
			//! nullチェック
			if(comboBox_ShipType.SelectedItem == null
			|| comboBox_ShipClass.SelectedItem == null
			|| comboBox_ShipName.SelectedItem == null)
				return;
			var unit_data = preset_data[(string)comboBox_ShipType.SelectedItem][(string)comboBox_ShipClass.SelectedItem][(string)comboBox_ShipName.SelectedItem];
			//! データを書き込む
			{
				var bindData = DataContext as TestBindObject;
				bool deep_ship_flg = (unit_data[(int)PresetData.KammusuFlg] == 0);  //! 深海棲艦フラグ
				//! 砲撃戦
				bindData.AttackGun = unit_data[(int)PresetData.Attack];
				if(deep_ship_flg) {
					comboBox_Attack_Gun_Level_0.SelectedIndex = 0;
					comboBox_Attack_Gun_Level_1.SelectedIndex = 0;
					comboBox_Attack_Gun_Level_2.SelectedIndex = 0;
					comboBox_Attack_Gun_Level_3.SelectedIndex = 0;
				}
				//! 砲撃戦(空母)
				bindData.AttackGunAir = unit_data[(int)PresetData.Attack];
				bindData.BombGunAir = unit_data[(int)PresetData.Bomb];
				bindData.TorpedoGunAir = unit_data[(int)PresetData.Torpedo2];
				if(deep_ship_flg) {
					//! 深海棲艦の場合、艦載機熟練度・装備改修度が載ってないので全て0にしておく
					comboBox_GunAir_Skill_0.SelectedIndex = 0;
					comboBox_GunAir_Skill_1.SelectedIndex = 0;
					comboBox_GunAir_Skill_2.SelectedIndex = 0;
					comboBox_GunAir_Skill_3.SelectedIndex = 0;
					comboBox_GunAir_Level_0.SelectedIndex = 0;
					comboBox_GunAir_Level_1.SelectedIndex = 0;
					comboBox_GunAir_Level_2.SelectedIndex = 0;
					comboBox_GunAir_Level_3.SelectedIndex = 0;
				}
				//! 雷撃戦
				bindData.Torpedo = unit_data[(int)PresetData.Torpedo1];
				if(deep_ship_flg) {
					comboBox_Torpedo_Level_0.SelectedIndex = 0;
					comboBox_Torpedo_Level_1.SelectedIndex = 0;
					comboBox_Torpedo_Level_2.SelectedIndex = 0;
					comboBox_Torpedo_Level_3.SelectedIndex = 0;
				}
				//! 航空戦
				if(unit_data[(int)PresetData.PAPB_Power] >= 0) {
					bindData.PowerAir = unit_data[(int)PresetData.PAPB_Power];
					comboBox_Air_Type.SelectedIndex = 1;
				} else {
					bindData.PowerAir = -unit_data[(int)PresetData.PAPB_Power];
					comboBox_Air_Type.SelectedIndex = 0;
				}
				bindData.SlotsAir = unit_data[(int)PresetData.SlotSize];
				if(deep_ship_flg) {
					comboBox_Air_Skill_0.SelectedIndex = 0;
					comboBox_Air_Skill_1.SelectedIndex = 0;
					comboBox_Air_Skill_2.SelectedIndex = 0;
					comboBox_Air_Skill_3.SelectedIndex = 0;
				}
				//! 対潜攻撃
				if(!deep_ship_flg && (bool)checkBox_Marriage.IsChecked) {
					bindData.AntiSubKammusu = unit_data[(int)PresetData.MarriageAntiSubBody];
				}else {
					bindData.AntiSubKammusu = unit_data[(int)PresetData.AntiSubBody];
				}
				bindData.AntiSubWeapons = unit_data[(int)PresetData.AntiSubWeapon];
				if(deep_ship_flg) {
					comboBox_AntiSub_Level_0.SelectedIndex = 0;
					comboBox_AntiSub_Level_1.SelectedIndex = 0;
					comboBox_AntiSub_Level_2.SelectedIndex = 0;
					comboBox_AntiSub_Level_3.SelectedIndex = 0;
				}
				//! 夜戦
				bindData.AttackNight = unit_data[(int)PresetData.Attack];
				bindData.TorpedoNight = unit_data[(int)PresetData.Torpedo1];
				if(deep_ship_flg) {
					comboBox_Attack_Night_Level_0.SelectedIndex = 0;
					comboBox_Attack_Night_Level_1.SelectedIndex = 0;
					comboBox_Attack_Night_Level_2.SelectedIndex = 0;
					comboBox_Attack_Night_Level_3.SelectedIndex = 0;
				}
				//! ラベル(その実テキストボックス)
				if(deep_ship_flg)
					bindData.HunterName = (string)comboBox_ShipClass.SelectedItem + (string)comboBox_ShipName.SelectedItem;
				else
					bindData.HunterName = (string)comboBox_ShipName.SelectedItem;
				if((bool)checkBox_Marriage.IsChecked && !deep_ship_flg)
					bindData.HunterName += "(ケッコン)";
			}
		}
		private void button_SetShipD_Click(object sender, RoutedEventArgs e) {
			//! nullチェック
			if(comboBox_ShipType.SelectedItem == null
			|| comboBox_ShipClass.SelectedItem == null
			|| comboBox_ShipName.SelectedItem == null)
				return;
			var unit_data = preset_data[(string)comboBox_ShipType.SelectedItem][(string)comboBox_ShipClass.SelectedItem][(string)comboBox_ShipName.SelectedItem];
			//! データを書き込む
			{
				var bindData = DataContext as TestBindObject;
				//! 砲撃戦
				var ship_type = (string)comboBox_ShipType.SelectedItem;
				var ship_class = (string)comboBox_ShipClass.SelectedItem;
				if(ship_type == "陸上型") {
					switch(ship_class) {
					case "集積地棲姫":
						comboBox_Enemy_Type.SelectedIndex = comboBox_Enemy_Type_Night.SelectedIndex = 2;
						break;
					case "砲台子鬼":
						comboBox_Enemy_Type.SelectedIndex = comboBox_Enemy_Type_Night.SelectedIndex = 3;
						break;
					case "離島棲姫":
						comboBox_Enemy_Type.SelectedIndex = comboBox_Enemy_Type_Night.SelectedIndex = 4;
						break;
					default:
						comboBox_Enemy_Type.SelectedIndex = comboBox_Enemy_Type_Night.SelectedIndex = 1;
						break;
					}
				}else {
					comboBox_Enemy_Type.SelectedIndex = comboBox_Enemy_Type_Night.SelectedIndex = 0;
				}
				//! 防御側設定
				bindData.Defense = unit_data[(int)PresetData.Defense];
				if(unit_data[(int)PresetData.KammusuFlg] == 1 && (bool)checkBox_Marriage.IsChecked) {
					bindData.MaxHP = bindData.NowHP = unit_data[(int)PresetData.MarriageHP];
				}else {
					bindData.MaxHP = bindData.NowHP = unit_data[(int)PresetData.HP];
				}
				if(unit_data[(int)PresetData.KammusuFlg] == 1) {
					checkBox_Kammusu.IsChecked = true;
				} else {
					checkBox_Kammusu.IsChecked = false;
				}
				//! ラベル(その実テキストボックス)
				if(unit_data[(int)PresetData.KammusuFlg] == 0)
					bindData.TargetName = (string)comboBox_ShipClass.SelectedItem + (string)comboBox_ShipName.SelectedItem;
				else
					bindData.TargetName = (string)comboBox_ShipName.SelectedItem;
				if((bool)checkBox_Marriage.IsChecked && unit_data[(int)PresetData.KammusuFlg] == 1)
					bindData.TargetName += "(ケッコン)";
			}
		}
		/// <summary>
		/// 艦娘フラグを切り替える処理
		/// </summary>
		private void checkBox_Kammusu_Checked(object sender, RoutedEventArgs e) {
			checkBox_Gimmick.IsEnabled = false;
			GeneralRouted(sender, e);
		}
		private void checkBox_Kammusu_Unchecked(object sender, RoutedEventArgs e) {
			checkBox_Gimmick.IsEnabled = true;
			GeneralRouted(sender, e);
		}

		/* 右クリック時の動作 */
		private void CopyHistText_Click(object sender, RoutedEventArgs e) {
			var histText = MakeHistText();
			System.Windows.Clipboard.SetText(histText);
		}
		private void CopyHistPic_Click(object sender, RoutedEventArgs e) {
			var stream = new System.IO.MemoryStream();
			chart.SaveImage(stream, System.Drawing.Imaging.ImageFormat.Bmp);
			var bmp = new System.Drawing.Bitmap(stream);
			System.Windows.Clipboard.SetDataObject(bmp);
		}
		private void SaveHistText_Click(object sender, RoutedEventArgs e) {
			var histText = MakeHistText();
			var sfd = new SaveFileDialog();
			sfd.FileName = "hist.csv";
			sfd.Filter = "CSVファイル(*.csv)|*.csv|すべてのファイル(*.*)|*.*";
			sfd.ShowDialog();
			// ファイルに書き込む
			// https://msdn.microsoft.com/library/ms182334.aspx
			// http://divakk.co.jp/aoyagi/csharp_tips_using.html
			using (var stream = sfd.OpenFile())
			using(var sw = new System.IO.StreamWriter(stream))
				sw.Write(histText);
		}
		private void SaveHistPic_Click(object sender, RoutedEventArgs e) {
			var sfd = new SaveFileDialog();
			sfd.FileName = "hist.png";
			sfd.Filter = "PNGファイル(*.png)|*.png|すべてのファイル(*.*)|*.*";
			sfd.ShowDialog();
			if(sfd.FileName != "") {
				chart.SaveImage(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
			}
		}
		private void CopyResult_Click(object sender, RoutedEventArgs e) {
			var bindData = DataContext as TestBindObject;
			System.Windows.Clipboard.SetText(bindData.StatusMessage);
		}

		/* 自動再計算用のテンプレ */
		private void AutoDrawHistogram() {
			if(autoCalcFlg)
				DrawHistogram();
		}
		private void GeneralPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
			AutoDrawHistogram();
		}
		private void GeneralRouted(object sender, RoutedEventArgs e) {
			AutoDrawHistogram();
		}
		private void GeneralSelectionChanged(object sender, SelectionChangedEventArgs e) {
			AutoDrawHistogram();
		}

		/* 計算用各種関数 */
		/// <summary>
		/// 計算処理
		/// </summary>
		private void CalcData() {
			// 「彩雲有り」「彩雲無し」のため、処理を分割する
			var hist = new Dictionary<int, int>();  //ヒストグラム(元)
			var count = loopCount[comboBox_TryCount.SelectedIndex];
			int[] damage_count = {0, 0, 0, 0};
			int[] status = {					//状態カウント
				0, 0, 0, 0, 0, 0,				//無傷～撃沈回数
				count + 1, -1, count + 1, -1,	//通常攻撃の下限と上限、クリティカルの下限と上限
				count + 1, -1, count + 1, -1,	//通常攻撃(強)の下限と上限、クリティカル(強)の下限と上限
				0,0,0,0};						//割合攻撃になった回数(通常、クリティカル)
				//彩雲が絡む場合、確率毎に按分する
				//「彩雲有り」の場合、同航戦：反航戦：丁字有利：丁字不利＝45：40：15：0
				//「彩雲無し」の場合、同航戦：反航戦：丁字有利：丁字不利＝45：30：15：10
			switch(comboBox_Position.SelectedIndex) {
			case 4:
				CalcDataSlave(0, hist, status, count * 45 / 100, damage_count);
				CalcDataSlave(1, hist, status, count * 40 / 100, damage_count);
				CalcDataSlave(2, hist, status, count * 15 / 100, damage_count);
				break;
			case 5:
				CalcDataSlave(0, hist, status, count * 45 / 100, damage_count);
				CalcDataSlave(1, hist, status, count * 30 / 100, damage_count);
				CalcDataSlave(2, hist, status, count * 15 / 100, damage_count);
				CalcDataSlave(3, hist, status, count * 10 / 100, damage_count);
				break;
			default:
				CalcDataSlave(comboBox_Position.SelectedIndex, hist, status, count, damage_count);
				break;
			}
			//ソート
			sorted_hist = new SortedDictionary<int, int>(hist);
			//平均ダメージの計算
			double averageDamage = 0.0;
			foreach(var it in hist) {
				averageDamage += 1.0 * it.Key * it.Value;
			}
			averageDamage /= count;
			//状態messageの更新
			var bindData = DataContext as TestBindObject;
			bindData.StatusMessage = "平均ダメージ：" + Math.Round(averageDamage, 3) + "\n";
			if(tabControl.SelectedIndex == TabIndexAir && comboBox_Air_Type.SelectedIndex == 0) {
				bindData.StatusMessage += "通常ダメージ(80%)：" + status[(int)StatusIndex.NormalMin] + "～" + status[(int)StatusIndex.NormalMax] + "\n";
				bindData.StatusMessage += "(割合ダメージ：" + Math.Round(100.0 * status[(int)StatusIndex.NormalPerDamage] / damage_count[0], 3) + "％)\n";
				bindData.StatusMessage += "クリティカル(80%)：" + status[(int)StatusIndex.CriticalMin] + "～" + status[(int)StatusIndex.CriticalMax] + "\n";
				bindData.StatusMessage += "(割合ダメージ：" + Math.Round(100.0 * status[(int)StatusIndex.CriticalPerDamage] / damage_count[1], 3) + "％)\n";
				bindData.StatusMessage += "通常ダメージ(150%)：" + status[(int)StatusIndex.NormalMinBig] + "～" + status[(int)StatusIndex.NormalMaxBig] + "\n";
				bindData.StatusMessage += "(割合ダメージ：" + Math.Round(100.0 * status[(int)StatusIndex.NormalPerDamageBig] / damage_count[2], 3) + "％)\n";
				bindData.StatusMessage += "クリティカル(150%)：" + status[(int)StatusIndex.CriticalMinBig] + "～" + status[(int)StatusIndex.CriticalMaxBig] + "\n";
				bindData.StatusMessage += "(割合ダメージ：" + Math.Round(100.0 * status[(int)StatusIndex.CriticalPerDamageBig] / damage_count[3], 3) + "％)\n";
			} else if(GetAttackCount() == 2) {
				bindData.StatusMessage += "ダメージ：" + status[(int)StatusIndex.NormalMin] + "～" + status[(int)StatusIndex.NormalMax] + "\n";
			} else {
				bindData.StatusMessage += "通常ダメージ：" + status[(int)StatusIndex.NormalMin] + "～" + status[(int)StatusIndex.NormalMax] + "\n";
				bindData.StatusMessage += "(割合ダメージ：" + Math.Round(100.0 * status[(int)StatusIndex.NormalPerDamage] / damage_count[0], 3) + "％)\n";
				bindData.StatusMessage += "クリティカル：" + status[(int)StatusIndex.CriticalMin] + "～" + status[(int)StatusIndex.CriticalMax] + "\n";
				bindData.StatusMessage += "(割合ダメージ：" + Math.Round(100.0 * status[(int)StatusIndex.CriticalPerDamage] / damage_count[1], 3) + "％)\n";
			}
			bindData.StatusMessage += "無傷率：" + Math.Round(100.0 * status[(int)StatusIndex.NoDamage] / count, 1) + "%\n";
			bindData.StatusMessage += "カスダメ率：" + Math.Round(100.0 * status[(int)StatusIndex.TooSmall] / count, 1) + "%\n";
			bindData.StatusMessage += "小破率：" + Math.Round(100.0 * status[(int)StatusIndex.Small] / count, 1) + "%\n";
			bindData.StatusMessage += "中破率：" + Math.Round(100.0 * status[(int)StatusIndex.Middle] / count, 1) + "%\n";
			bindData.StatusMessage += "大破率：" + Math.Round(100.0 * status[(int)StatusIndex.Large] / count, 1) + "%\n";
			bindData.StatusMessage += "撃沈率：" + Math.Round(100.0 * status[(int)StatusIndex.Destroyed] / count, 1) + "%";
		}
		/// <summary>
		/// 計算処理(分割後)
		/// </summary>
		private void CalcDataSlave(int type, Dictionary<int, int> hist, int[] status, int loops, int[] damage_count) {
			// baseAttackValue～attackValueAfterCapまでは{ダメージ小, ダメージ大}、
			// lastAttackValueは{ダメージ小, ダメージ大, ダメージ小クリティカル, ダメージ大クリティカル}
			// 基本攻撃力を算出する
			var baseAttackValue = CalcBaseAttack();
			// キャップ前攻撃力を出す
			var attackValueBeforeCap = CalcAttackBeforeCap(baseAttackValue, type);
			// キャップ後攻撃力を出す
			var attackValueAfterCap = CalcAttackAfterCap(attackValueBeforeCap);
			// 最終攻撃力を出す
			var lastAttackValue = CalcLastAttack(attackValueAfterCap);
			// ダメージを算出し、ヒストグラムを取る
			CalcHistogram(lastAttackValue, hist, status, loops, damage_count);
		}
		/// <summary>
		/// 基本攻撃力を出す
		/// </summary>
		private double[] CalcBaseAttack() {
			var bindData = DataContext as TestBindObject;
			var baseAttackValue = 0.0;
			switch(tabControl.SelectedIndex) {
			case TabIndexGun:
				{
					baseAttackValue = bindData.AttackGun + 5;
					// 装備改修値
					double[] param = { 1.0, 1.5, 0.75, 0.0, 0.0 };
					baseAttackValue += param[comboBox_Attack_Gun_Type_0.SelectedIndex] * Math.Sqrt(comboBox_Attack_Gun_Level_0.SelectedIndex);
					baseAttackValue += param[comboBox_Attack_Gun_Type_1.SelectedIndex] * Math.Sqrt(comboBox_Attack_Gun_Level_1.SelectedIndex);
					baseAttackValue += param[comboBox_Attack_Gun_Type_2.SelectedIndex] * Math.Sqrt(comboBox_Attack_Gun_Level_2.SelectedIndex);
					baseAttackValue += param[comboBox_Attack_Gun_Type_3.SelectedIndex] * Math.Sqrt(comboBox_Attack_Gun_Level_3.SelectedIndex);
					// 連合艦隊補正
					if(comboBox_FleetOption.SelectedIndex > 0) {
						if(comboBox_Formation.SelectedIndex >= 5) {
							// 連合艦隊(自艦隊視点)
							double[] add_attack = { 2, 10, 10, -5, -5, 10 };
							baseAttackValue += add_attack[comboBox_FleetOption.SelectedIndex - 1];
						} else {
							// 連合艦隊(敵艦隊視点)
							double[] add_attack = { 10, 5, 5, -5, 10, +5 };
							baseAttackValue += add_attack[comboBox_FleetOption.SelectedIndex - 1];
						}
					}
				}
				return new double[] { baseAttackValue, baseAttackValue };
			case TabIndexGunAir:
				{
					double temp = bindData.AttackGunAir;
					if(!(bool)checkBox_AF.IsChecked)
						temp += bindData.TorpedoGunAir + (int)(bindData.BombGunAir * 1.3);
					// 装備改修度
					temp += Math.Sqrt(comboBox_GunAir_Level_0.SelectedIndex);
					temp += Math.Sqrt(comboBox_GunAir_Level_1.SelectedIndex);
					temp += Math.Sqrt(comboBox_GunAir_Level_2.SelectedIndex);
					temp += Math.Sqrt(comboBox_GunAir_Level_3.SelectedIndex);
					// 連合艦隊補正
					if(comboBox_FleetOption.SelectedIndex > 0) {
						if(comboBox_Formation.SelectedIndex >= 5) {
							// 連合艦隊(自艦隊視点)
							double[] add_attack = { 2, 10, 10, -5, -5, 10 };
							temp += add_attack[comboBox_FleetOption.SelectedIndex - 1];
						} else {
							// 連合艦隊(敵艦隊視点)
							double[] add_attack = { 10, 5, 5, -5, 10, +5 };
							temp += add_attack[comboBox_FleetOption.SelectedIndex - 1];
						}
					}
					//
					baseAttackValue = (int)(temp * 1.5) + 55;
				}
				return new double[] { baseAttackValue, baseAttackValue };
			case TabIndexTorpedo:
				baseAttackValue = bindData.Torpedo + 5;
				// 装備改修値
				baseAttackValue += 1.2 * Math.Sqrt(comboBox_Torpedo_Level_0.SelectedIndex);
				baseAttackValue += 1.2 * Math.Sqrt(comboBox_Torpedo_Level_1.SelectedIndex);
				baseAttackValue += 1.2 * Math.Sqrt(comboBox_Torpedo_Level_2.SelectedIndex);
				baseAttackValue += 1.2 * Math.Sqrt(comboBox_Torpedo_Level_3.SelectedIndex);
				// 連合艦隊補正
				if(comboBox_FleetOption.SelectedIndex > 0) {
					baseAttackValue -= 5;
				}
				return new double[] { baseAttackValue, baseAttackValue };
			case TabIndexAir:
				baseAttackValue = (bindData.PowerAir * Math.Sqrt(bindData.SlotsAir) + 25);
				if(comboBox_Air_Type.SelectedIndex == 0) {
					return new double[] { baseAttackValue * 0.8, baseAttackValue * 1.5 };
				}else {
					return new double[] { baseAttackValue, baseAttackValue };
				}
			case TabIndexAntiSub:
				// 素対潜
				baseAttackValue += Math.Sqrt(bindData.AntiSubKammusu) * 2;
				// 装備対潜
				baseAttackValue += 1.5 * bindData.AntiSubWeapons;
				// 装備改修値
				if(comboBox_AntiSub.SelectedIndex == 0) {
					baseAttackValue += Math.Sqrt(comboBox_AntiSub_Level_0.SelectedIndex);
					baseAttackValue += Math.Sqrt(comboBox_AntiSub_Level_1.SelectedIndex);
					baseAttackValue += Math.Sqrt(comboBox_AntiSub_Level_2.SelectedIndex);
					baseAttackValue += Math.Sqrt(comboBox_AntiSub_Level_3.SelectedIndex);
				}
				// 対潜定数
				baseAttackValue += (comboBox_AntiSub.SelectedIndex == 0 ? 13 : 8);
				return new double[] { baseAttackValue, baseAttackValue };
			case TabIndexNight:
				baseAttackValue = bindData.AttackNight + ((bool)checkBox_Night_Trailer.IsChecked ? 5 : 0);
				// 装備改修値
				baseAttackValue += Math.Sqrt(comboBox_Attack_Night_Level_0.SelectedIndex);
				baseAttackValue += Math.Sqrt(comboBox_Attack_Night_Level_1.SelectedIndex);
				baseAttackValue += Math.Sqrt(comboBox_Attack_Night_Level_2.SelectedIndex);
				baseAttackValue += Math.Sqrt(comboBox_Attack_Night_Level_3.SelectedIndex);
				// 対地攻撃では雷装値無効
				if(comboBox_Enemy_Type_Night.SelectedIndex == 0) {
					baseAttackValue += bindData.TorpedoNight;
				}
				return new double[] { baseAttackValue, baseAttackValue };
			default:
				return new double[] { baseAttackValue, baseAttackValue };
			}
		}
		/// <summary>
		/// キャップ前攻撃力を出す
		/// </summary>
		private double[] CalcAttackBeforeCap(double[] baseAttackValue, int type) {
			var bindData = DataContext as TestBindObject;
			var attackValueBeforeCap = baseAttackValue;
			for(var i = 0; i < 2; ++i) {
				// キャップ前陸上特効
				if(tabControl.SelectedIndex == TabIndexGun && comboBox_Enemy_Type.SelectedIndex != 0) {
					// 三式弾
					if(comboBox_Enemy_Type.SelectedIndex == 1 || comboBox_Enemy_Type.SelectedIndex == 2) {
						if((bool)checkBox_Sanshiki.IsChecked)
							attackValueBeforeCap[i] *= 2.5;
					}
					//! 砲台子鬼
					if(comboBox_Enemy_Type.SelectedIndex == 3) {
						// 艦種
						if((bool)checkBox_DDCL.IsChecked)
							attackValueBeforeCap[i] *= 1.4;
						// 大発
						double[] param1 = { 1.0, 1.80, 2.15, 3.0 };
						switch(comboBox_Landing_Craft.SelectedIndex) {
						case 0:
							//! 大発なし
							attackValueBeforeCap[i] *= param1[comboBox_Landing_Craft.SelectedIndex];
							break;
						case 1:
							//! 大発
							attackValueBeforeCap[i] *= param1[comboBox_Landing_Craft.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Gun_Level_0.SelectedIndex / 50);
							break;
						case 2:
							//! 陸戦隊x1
							attackValueBeforeCap[i] *= param1[comboBox_Landing_Craft.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Gun_Level_0.SelectedIndex / 50);
							break;
						case 3:
							//! 陸戦隊x2
							attackValueBeforeCap[i] *= param1[comboBox_Landing_Craft.SelectedIndex] * (1.0 + 1.0 * (comboBox_Attack_Gun_Level_0.SelectedIndex + comboBox_Attack_Gun_Level_1.SelectedIndex) / 100);
							break;
						}
						// カミ車
						double[] param2 = { 1.0, 2.4, 3.2 };
						switch(comboBox_KaMi.SelectedIndex) {
						case 0:
							//! 0個
							attackValueBeforeCap[i] *= param2[comboBox_KaMi.SelectedIndex];
							break;
						case 1:
							//! 1個
							attackValueBeforeCap[i] *= param2[comboBox_KaMi.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Gun_Level_2.SelectedIndex / 30);
							break;
						case 2:
							//! 2個
							attackValueBeforeCap[i] *= param2[comboBox_KaMi.SelectedIndex] * (1.0 + 1.0 * (comboBox_Attack_Gun_Level_2.SelectedIndex + comboBox_Attack_Gun_Level_3.SelectedIndex) / 60);
							break;
						}
						// WG42
						double[] param3 = { 1.0, 1.6, 2.72, 2.72, 2.72 };
						attackValueBeforeCap[i] *= param3[comboBox_WG42.SelectedIndex];
						// 水爆水戦
						if((bool)checkBox_WBWF.IsChecked)
							attackValueBeforeCap[i] *= 1.5;
						// 徹甲弾
						if(comboBox_Shell.SelectedIndex != 0)
							attackValueBeforeCap[i] *= 1.85;
					}
					//! 離島棲鬼
					if(comboBox_Enemy_Type.SelectedIndex == 4) {
						// WG42
						double[] param1 = { 1.0, 1.4, 2.1, 2.1, 2.1 };
						attackValueBeforeCap[i] *= param1[comboBox_WG42.SelectedIndex];
						// 三式弾
						if((bool)checkBox_Sanshiki.IsChecked)
							attackValueBeforeCap[i] *= 1.75;
					}
					//! WG加算特効
					{
						double[] param = { 0, 75, 110, 140, 160 };
						attackValueBeforeCap[i] += param[comboBox_WG42.SelectedIndex];
					}
				}
				if(tabControl.SelectedIndex == TabIndexNight && comboBox_Enemy_Type_Night.SelectedIndex != 0) {
					// 三式弾
					if(comboBox_Enemy_Type_Night.SelectedIndex == 1 || comboBox_Enemy_Type_Night.SelectedIndex == 2) {
						if((bool)checkBox_Sanshiki_Night.IsChecked)
							attackValueBeforeCap[i] *= 2.5;
					}
					//! 砲台子鬼
					if(comboBox_Enemy_Type_Night.SelectedIndex == 3) {
						// 艦種
						if((bool)checkBox_DDCL_Night.IsChecked)
							attackValueBeforeCap[i] *= 1.4;
						// 大発
						double[] param1 = { 1.0, 1.80, 2.15, 3.0 };
						switch(comboBox_Landing_Craft_Night.SelectedIndex) {
						case 0:
							//! 大発なし
							attackValueBeforeCap[i] *= param1[comboBox_Landing_Craft_Night.SelectedIndex];
							break;
						case 1:
							//! 大発
							attackValueBeforeCap[i] *= param1[comboBox_Landing_Craft_Night.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Night_Level_0.SelectedIndex / 50);
							break;
						case 2:
							//! 陸戦隊x1
							attackValueBeforeCap[i] *= param1[comboBox_Landing_Craft_Night.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Night_Level_0.SelectedIndex / 50);
							break;
						case 3:
							//! 陸戦隊x2
							attackValueBeforeCap[i] *= param1[comboBox_Landing_Craft_Night.SelectedIndex] * (1.0 + 1.0 * (comboBox_Attack_Night_Level_0.SelectedIndex + comboBox_Attack_Night_Level_1.SelectedIndex) / 100);
							break;
						}
						// カミ車
						double[] param2 = { 1.0, 2.4, 3.2 };
						switch(comboBox_KaMi_Night.SelectedIndex) {
						case 0:
							//! 0個
							attackValueBeforeCap[i] *= param2[comboBox_KaMi_Night.SelectedIndex];
							break;
						case 1:
							//! 1個
							attackValueBeforeCap[i] *= param2[comboBox_KaMi_Night.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Night_Level_2.SelectedIndex / 30);
							break;
						case 2:
							//! 2個
							attackValueBeforeCap[i] *= param2[comboBox_KaMi_Night.SelectedIndex] * (1.0 + 1.0 * (comboBox_Attack_Night_Level_2.SelectedIndex + comboBox_Attack_Night_Level_3.SelectedIndex) / 60);
							break;
						}
						// WG42
						double[] param3 = { 1.0, 1.6, 2.72, 2.72, 2.72 };
						attackValueBeforeCap[i] *= param3[comboBox_WG42_Night.SelectedIndex];
						// 水爆水戦
						if((bool)checkBox_WBWF_Night.IsChecked)
							attackValueBeforeCap[i] *= 1.5;
						// 徹甲弾
						if((bool)checkBox_Shiell_Night.IsChecked)
							attackValueBeforeCap[i] *= 1.85;
					}
					//! 離島棲鬼
					if(comboBox_Enemy_Type_Night.SelectedIndex == 4) {
						// WG42
						double[] param1 = { 1.0, 1.4, 2.1, 2.1, 2.1 };
						attackValueBeforeCap[i] *= param1[comboBox_WG42_Night.SelectedIndex];
						// 三式弾
						if((bool)checkBox_Sanshiki_Night.IsChecked)
							attackValueBeforeCap[i] *= 1.75;
					}
					//! WG加算特効
					{
						double[] param = { 0, 75, 110, 140, 160 };
						attackValueBeforeCap[i] += param[comboBox_WG42_Night.SelectedIndex];
					}
				}
				// キャップ前補正
				//交戦形態補正
				if(tabControl.SelectedIndex == TabIndexGun
				|| tabControl.SelectedIndex == TabIndexGunAir
				|| tabControl.SelectedIndex == TabIndexTorpedo
				|| tabControl.SelectedIndex == TabIndexAntiSub) {
					double[] param = { 1, 0.8, 1.2, 0.6 };
					attackValueBeforeCap[i] *= param[type];
				}
				//陣形補正
				if(tabControl.SelectedIndex == TabIndexGun
				|| tabControl.SelectedIndex == TabIndexGunAir) {
					double[] param = { 1.0, 0.8, 0.7, 0.6, 0.6, 0.8, 1.0, 0.7, 1.1 };
					attackValueBeforeCap[i] *= param[comboBox_Formation.SelectedIndex];
				} else if(tabControl.SelectedIndex == TabIndexTorpedo) {
					double[] param = { 1.0, 0.8, 0.7, 0.6, 0.6, 0.7, 0.9, 0.6, 1.0 };
					attackValueBeforeCap[i] *= param[comboBox_Formation.SelectedIndex];
				} else if(tabControl.SelectedIndex == TabIndexAntiSub) {
					double[] param = { 0.6, 0.8, 1.2, 1.0, 1.3, 1.1, 1.0, 0.7 };
					attackValueBeforeCap[i] *= param[comboBox_Formation.SelectedIndex];
				}
				//夜戦特殊攻撃補正
				if(tabControl.SelectedIndex == TabIndexNight) {
					double[] param = { 1.0, 1.5, 1.3, 2.0, 1.75, 1.2 };
					attackValueBeforeCap[i] *= param[comboBox_Night_Type.SelectedIndex];
				}
				//損傷補正
				if(tabControl.SelectedIndex != TabIndexAir) {
					if(tabControl.SelectedIndex == TabIndexTorpedo && (bool)checkBox_FirstTorpedo.IsChecked) {
						double[] param = { 1, 0.8, 0.0 };
						attackValueBeforeCap[i] *= param[comboBox_Damage.SelectedIndex];
					} else {
						double[] param = { 1, 0.7, 0.4 };
						attackValueBeforeCap[i] *= param[comboBox_Damage.SelectedIndex];
					}
				}
				//対潜シナジー補正
				if(tabControl.SelectedIndex == TabIndexAntiSub) {
					if((bool)checkBox_AntiSubSynergy.IsChecked)
						attackValueBeforeCap[i] *= 1.15;
				}
				//軽巡軽量砲補正
				if(tabControl.SelectedIndex == TabIndexGun) {
					attackValueBeforeCap[i] += Math.Sqrt(comboBox_One_LightLight.SelectedIndex) + 2.0 * Math.Sqrt(comboBox_Two_LightLight.SelectedIndex);
				}
				if(tabControl.SelectedIndex == TabIndexNight) {
					attackValueBeforeCap[i] += Math.Sqrt(comboBox_One_LightLight_Night.SelectedIndex) + 2.0 * Math.Sqrt(comboBox_Two_LightLight_Night.SelectedIndex);
				}
			}
			return attackValueBeforeCap;
		}
		/// <summary>
		/// キャップ後攻撃力を出す
		/// </summary>
		private double[] CalcAttackAfterCap(double[] attackValueBeforeCap) {
			var attackValueAfterCap = attackValueBeforeCap;
			for(var i = 0; i < 2; ++i) {
				switch (tabControl.SelectedIndex) {
				case TabIndexGun:
				case TabIndexGunAir:
					attackValueAfterCap[i] = CalcCap(attackValueBeforeCap[i], 180.0);
					break;
				case TabIndexTorpedo:
				case TabIndexAir:
					attackValueAfterCap[i] = CalcCap(attackValueBeforeCap[i], 150.0);
					break;
				case TabIndexAntiSub:
					attackValueAfterCap[i] = CalcCap(attackValueBeforeCap[i], 100.0);
					break;
				case TabIndexNight:
					attackValueAfterCap[i] = CalcCap(attackValueBeforeCap[i], 300.0);
					break;
				}
			}
			return attackValueAfterCap;
		}
		/// <summary>
		/// 最終攻撃力を出す
		/// </summary>
		private double[] CalcLastAttack(double[] attackValueAfterCap) {
			var lastAttackValue = new double[4];
			for(var i = 0; i < 2; ++i) {
				double lastAttackValueTemp = attackValueAfterCap[i];
				// 集積地棲姫特効
				if(tabControl.SelectedIndex == TabIndexGun && comboBox_Enemy_Type.SelectedIndex == 2) {
					double[] param1 = { 1.0, 1.25, 1.625, 1.625, 1.625 };
					lastAttackValueTemp = (lastAttackValueTemp * param1[comboBox_WG42.SelectedIndex]);
					double[] param2 = { 1.0, 1.0, 1.3, 2.08 };
					switch(comboBox_Landing_Craft.SelectedIndex) {
					case 0:
						//! 大発なし
						lastAttackValueTemp *= param2[comboBox_Landing_Craft.SelectedIndex];
						break;
					case 1:
						//! 大発
						lastAttackValueTemp *= param2[comboBox_Landing_Craft.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Gun_Level_0.SelectedIndex / 50);
						break;
					case 2:
						//! 陸戦隊x1
						lastAttackValueTemp *= param2[comboBox_Landing_Craft.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Gun_Level_0.SelectedIndex / 50);
						break;
					case 3:
						//! 陸戦隊x2
						lastAttackValueTemp *= param2[comboBox_Landing_Craft.SelectedIndex] * (1.0 + 1.0 * (comboBox_Attack_Gun_Level_0.SelectedIndex + comboBox_Attack_Gun_Level_1.SelectedIndex) / 100);
						break;
					}
					double[] param3 = { 1.0, 1.7, 2.5 };
					switch(comboBox_KaMi.SelectedIndex) {
					case 0:
						//! 0個
						lastAttackValueTemp *= param3[comboBox_KaMi.SelectedIndex];
						break;
					case 1:
						//! 1個
						lastAttackValueTemp *= param3[comboBox_KaMi.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Gun_Level_2.SelectedIndex / 30);
						break;
					case 2:
						//! 2個
						lastAttackValueTemp *= param3[comboBox_KaMi.SelectedIndex] * (1.0 + 1.0 * (comboBox_Attack_Gun_Level_2.SelectedIndex + comboBox_Attack_Gun_Level_3.SelectedIndex) / 60);
						break;
					}
				}
				if(tabControl.SelectedIndex == TabIndexNight && comboBox_Enemy_Type_Night.SelectedIndex == 2) {
					double[] param1 = { 1.0, 1.25, 1.625, 1.625, 1.625 };
					lastAttackValueTemp = (lastAttackValueTemp * param1[comboBox_WG42_Night.SelectedIndex]);
					double[] param2 = { 1.0, 1.0, 1.3, 2.08 };
					switch(comboBox_Landing_Craft_Night.SelectedIndex) {
					case 0:
						//! 大発なし
						lastAttackValueTemp *= param2[comboBox_Landing_Craft_Night.SelectedIndex];
						break;
					case 1:
						//! 大発
						lastAttackValueTemp *= param2[comboBox_Landing_Craft_Night.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Night_Level_0.SelectedIndex / 50);
						break;
					case 2:
						//! 陸戦隊x1
						lastAttackValueTemp *= param2[comboBox_Landing_Craft_Night.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Night_Level_0.SelectedIndex / 50);
						break;
					case 3:
						//! 陸戦隊x2
						lastAttackValueTemp *= param2[comboBox_Landing_Craft_Night.SelectedIndex] * (1.0 + 1.0 * (comboBox_Attack_Night_Level_0.SelectedIndex + comboBox_Attack_Night_Level_1.SelectedIndex) / 100);
						break;
					}
					double[] param3 = { 1.0, 1.7, 2.5 };
					switch(comboBox_KaMi_Night.SelectedIndex) {
					case 0:
						//! 0個
						lastAttackValueTemp *= param3[comboBox_KaMi_Night.SelectedIndex];
						break;
					case 1:
						//! 1個
						lastAttackValueTemp *= param3[comboBox_KaMi_Night.SelectedIndex] * (1.0 + 1.0 * comboBox_Attack_Night_Level_2.SelectedIndex / 30);
						break;
					case 2:
						//! 2個
						lastAttackValueTemp *= param3[comboBox_KaMi_Night.SelectedIndex] * (1.0 + 1.0 * (comboBox_Attack_Night_Level_2.SelectedIndex + comboBox_Attack_Night_Level_3.SelectedIndex) / 60);
						break;
					}
				}
				lastAttackValueTemp = (int)lastAttackValueTemp;
				// 徹甲弾特効
				if(tabControl.SelectedIndex == TabIndexGun) {
					if(comboBox_Enemy_Type.SelectedIndex != 4) {
						double[] param = { 1.0, 1.08, 1.1, 1.15, 1.15 };
						lastAttackValueTemp = (int)(lastAttackValueTemp * param[comboBox_Shell.SelectedIndex]);
					}
				}
				// クリティカル補正
				double lastAttackValueWithoutCL = (int)lastAttackValueTemp;
				var lastAttackValueWithCL = lastAttackValueTemp * 1.5;
				// 熟練度補正
				switch(tabControl.SelectedIndex) {
				case TabIndexGunAir:
					{
						var airLevelWeight = 1.0;
						airLevelWeight += comboBox_GunAir_Skill_0.SelectedIndex * 0.2 / 7;
						airLevelWeight += comboBox_GunAir_Skill_1.SelectedIndex * 0.1 / 7;
						airLevelWeight += comboBox_GunAir_Skill_2.SelectedIndex * 0.1 / 7;
						airLevelWeight += comboBox_GunAir_Skill_3.SelectedIndex * 0.1 / 7;
						lastAttackValueWithCL = (int)(lastAttackValueWithCL * airLevelWeight);
					}
					break;
				case TabIndexAir:
					{
						var airLevelWeight = 1.0;
						airLevelWeight += comboBox_Air_Skill_0.SelectedIndex * 0.2 / 7;
						airLevelWeight += comboBox_Air_Skill_0.SelectedIndex * 0.1 / 7;
						airLevelWeight += comboBox_Air_Skill_0.SelectedIndex * 0.1 / 7;
						airLevelWeight += comboBox_Air_Skill_0.SelectedIndex * 0.1 / 7;
						lastAttackValueWithCL = (int)(lastAttackValueWithCL * airLevelWeight);
					}
					break;
				case TabIndexAntiSub:
					if(comboBox_AntiSub.SelectedIndex == 1) {
						// 熟練度補正
						var airLevelWeight = 1.0;
						airLevelWeight += Limit(comboBox_AntiSub_Level_0.SelectedIndex, 0, 7) * 0.2 / 7;
						airLevelWeight += Limit(comboBox_AntiSub_Level_1.SelectedIndex, 0, 7) * 0.1 / 7;
						airLevelWeight += Limit(comboBox_AntiSub_Level_2.SelectedIndex, 0, 7) * 0.1 / 7;
						airLevelWeight += Limit(comboBox_AntiSub_Level_3.SelectedIndex, 0, 7) * 0.1 / 7;
						lastAttackValueWithCL = (int)(lastAttackValueWithCL * airLevelWeight);
					}
					break;
				default:
					lastAttackValueWithCL = (int)lastAttackValueWithCL;
					break;
				}
				// 触接・弾着補正
				switch(tabControl.SelectedIndex) {
				case TabIndexGun:
					{
						double[] param = { 1.0, 1.5, 1.3, 1.2, 1.1, 1.2 };
						lastAttackValueWithoutCL = lastAttackValueWithoutCL * param[comboBox_Watch.SelectedIndex];
						lastAttackValueWithCL = lastAttackValueWithCL * param[comboBox_Watch.SelectedIndex];
					}
					break;
				case TabIndexAir:
					{
						double[] param = { 1.0, 1.12, 1.12, 1.17, 1.2 };
						lastAttackValueWithoutCL = lastAttackValueWithoutCL * param[comboBox_Air_Trailer.SelectedIndex];
						lastAttackValueWithCL = lastAttackValueWithCL * param[comboBox_Air_Trailer.SelectedIndex];
					}
					break;
				}
				lastAttackValue[i] = lastAttackValueWithoutCL;
				lastAttackValue[i + 2] = lastAttackValueWithCL;
			}
			return lastAttackValue;
		}
		/// <summary>
		/// 攻撃回数を出す
		/// </summary>
		private int GetAttackCount() {
			if((bool)checkBox_Twice.IsChecked) {
				if(tabControl.SelectedIndex == TabIndexGun && comboBox_Watch.SelectedIndex == 5)
					return 2;
				if(tabControl.SelectedIndex == TabIndexNight) {
					var type = comboBox_Night_Type.SelectedIndex;
					if(type == 1 || type == 2 || type == 5)
						return 2;
				}
			}
			return 1;
		}
		/// <summary>
		/// ヒストグラムを出す
		/// </summary>
		private void CalcHistogram(double[] lastAttackValue, Dictionary<int, int> hist, int[] status, int loops, int[] damage_count) {
			var bindData = DataContext as TestBindObject;
			// 初期設定
			var defense = bindData.Defense;
			var nowHP = bindData.NowHP;
			var maxHP = bindData.MaxHP;
			double ammo;
			{
				double[] ammoWeight = { 1.0, 0.8, 0.4, 0.0 };
				if(comboBox_AmmoPer.SelectedIndex != ammoWeight.Count()) {
					ammo = ammoWeight[comboBox_AmmoPer.SelectedIndex];
				}else {
					var ammo_int = bindData.AmmoParam;
					if(ammo_int < 0)
						ammo_int = 0;
					if(ammo_int > 50)
						ammo_int = 50;
					ammo = 1.0 * ammo_int / 50;
				}
			}
			// 攻撃回数を決定する
			var attackCount = GetAttackCount();
			// ギミックによる防御力減少量を決定
			var defenseGain = 0.0;
			if(!(bool)checkBox_Kammusu.IsChecked && (bool)checkBox_Gimmick.IsChecked) {
				defenseGain = -23.0;
			}
			// ループ
			bool stopper_flg = (nowHP * 4 > maxHP);
			for(int i = 0; i < loops; ++i) {
				var nowHP2 = nowHP;
				var all_damage = 0;
				bool criticalFlg = false;
				bool bigDamageFlg = false;
				for(var j = 0; j < attackCount; ++j) {
					// ダメージ計算
					var defenseValue = defense * 0.7 + rand.Next(defense) * 0.6 + defenseGain;
					int damage;
					criticalFlg = false;
					bigDamageFlg = false;
					if(rand.NextDouble() * 1000 < slider_Critical.Value) {
						// クリティカル
						criticalFlg = true;
						if(tabControl.SelectedIndex == TabIndexAir && comboBox_Air_Type.SelectedIndex == 0) {
							if(rand.Next(2) == 1) {
								bigDamageFlg = true;
								++damage_count[3];
								damage = (int)((lastAttackValue[3] - defenseValue) * ammo);
							} else {
								++damage_count[1];
								damage = (int)((lastAttackValue[2] - defenseValue) * ammo);
							}
						} else {
							++damage_count[1];
							damage = (int)((lastAttackValue[2] - defenseValue) * ammo);
						}
					} else {
						// 非クリティカル
						if(tabControl.SelectedIndex == TabIndexAir && comboBox_Air_Type.SelectedIndex == 0) {
							if(rand.Next(2) == 1) {
								bigDamageFlg = true;
								++damage_count[2];
								damage = (int)((lastAttackValue[1] - defenseValue) * ammo);
							} else {
								++damage_count[0];
								damage = (int)((lastAttackValue[0] - defenseValue) * ammo);
							}
						} else {
							++damage_count[0];
							damage = (int)((lastAttackValue[0] - defenseValue) * ammo);
						}
					}
					//0以下の際の処理(カスダメ)
					if(damage <= 0) {
						if(criticalFlg) {
							if(bigDamageFlg)
								++status[(int)StatusIndex.CriticalPerDamageBig];
							else
								++status[(int)StatusIndex.CriticalPerDamage];
						} else {
							if(bigDamageFlg)
								++status[(int)StatusIndex.NormalPerDamageBig];
							else
								++status[(int)StatusIndex.NormalPerDamage];
						}
						damage = (int)(nowHP2 * 0.06 + rand.Next(nowHP2) * 0.08);
					}
					//艦娘で、更に現在耐久以上だった際の処理(大破ストッパー)
					if(damage >= nowHP2 && (bool)checkBox_Kammusu.IsChecked) {
						//大破：最大耐久の25％「以下」
						if(stopper_flg) {
							damage = (int)(nowHP2 * 0.5 + rand.Next(nowHP2) * 0.3);
						}
					}
					nowHP2 -= damage;
					all_damage += damage;
				}
				// ヒストグラムカウント
				if(hist.ContainsKey(all_damage)) {
					++hist[all_damage];
				} else {
					hist[all_damage] = 1;
				}
				// 状態カウント
				if(nowHP == nowHP2) {
					++status[(int)StatusIndex.NoDamage];
				} else if(nowHP2 * 4 > maxHP * 3) {
					++status[(int)StatusIndex.TooSmall];
				} else if(nowHP2 * 2 > maxHP) {
					++status[(int)StatusIndex.Small];
				} else if(nowHP2 * 4 > maxHP) {
					++status[(int)StatusIndex.Middle];
				} else if(nowHP2 > 0) {
					++status[(int)StatusIndex.Large];
				} else {
					++status[(int)StatusIndex.Destroyed];
				}
				// 上下限カウント
				if(attackCount == 1) {
					if(criticalFlg) {
						if(bigDamageFlg) {
							status[(int)StatusIndex.CriticalMinBig] = Math.Min(status[(int)StatusIndex.CriticalMinBig], all_damage);
							status[(int)StatusIndex.CriticalMaxBig] = Math.Max(status[(int)StatusIndex.CriticalMaxBig], all_damage);
						} else {
							status[(int)StatusIndex.CriticalMin] = Math.Min(status[(int)StatusIndex.CriticalMin], all_damage);
							status[(int)StatusIndex.CriticalMax] = Math.Max(status[(int)StatusIndex.CriticalMax], all_damage);
						}
					} else {
						if(bigDamageFlg) {
							status[(int)StatusIndex.NormalMinBig] = Math.Min(status[(int)StatusIndex.NormalMinBig], all_damage);
							status[(int)StatusIndex.NormalMaxBig] = Math.Max(status[(int)StatusIndex.NormalMaxBig], all_damage);
						} else {
							status[(int)StatusIndex.NormalMin] = Math.Min(status[(int)StatusIndex.NormalMin], all_damage);
							status[(int)StatusIndex.NormalMax] = Math.Max(status[(int)StatusIndex.NormalMax], all_damage);
						}
					}
				} else {
					status[(int)StatusIndex.NormalMin] = Math.Min(status[(int)StatusIndex.NormalMin], all_damage);
					status[(int)StatusIndex.NormalMax] = Math.Max(status[(int)StatusIndex.NormalMax], all_damage);
				}
			}
			// 特殊処理
			if(slider_Critical.Value == 0) {
				status[(int)StatusIndex.CriticalMin] = status[(int)StatusIndex.CriticalMax] = - 1;
				status[(int)StatusIndex.CriticalMinBig] = status[(int)StatusIndex.CriticalMaxBig] = -1;
			}
		}
		/// <summary>
		/// 値を制限する
		/// </summary>
		private int Limit(int a, int min, int max) {
			if(a < min)
				return min;
			if(a > max)
				return max;
			return a;
		}
		/// <summary>
		/// キャップ計算
		/// </summary>
		private double CalcCap(double x, double cap) {
			if(x < cap)
				return x;
			else
				return cap + Math.Sqrt(x - cap);
		}
	}
}
