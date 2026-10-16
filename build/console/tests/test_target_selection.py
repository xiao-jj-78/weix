"""Selection boundaries must not cause actions on unselected users."""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
from unittest import TestCase
from unittest.mock import Mock, patch
from console_app import config
from console_app.tasks import TaskRunner

spec = importlib.util.spec_from_file_location('target_engine', Path(__file__).parents[1] / 'engines/replica_like.py')
engine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(engine)

class Response(dict):
    is_success = True

class SelectedUserTests(TestCase):
    def test_invalid_or_blank_authors_never_expand_to_all_users(self):
        for value in ('[""]', '{}', '[1]', '["甲\\u0000乙"]'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                engine.selected_authors(authors_json=value)
        moment = Mock()
        with self.assertRaises(ValueError):
            engine.run_selected_round(moment, [], '', 5)
        moment.Like.assert_not_called()

    def test_selected_users_are_exact_and_duplicates_removed(self):
        selected = engine.selected_authors(authors_json='[" 甲 ","乙","甲"]')
        moment = Mock()
        moment.find_moment.side_effect = [SimpleNamespace(publisher='甲',text='关键词'),SimpleNamespace(publisher='乙',text='关键词')]
        moment.Like.return_value = Response(message='test')
        self.assertEqual(engine.run_selected_round(moment, selected, '关键词', 5), 0)
        self.assertEqual([c.kwargs['publisher'] for c in moment.find_moment.call_args_list], ['甲','乙'])
        self.assertTrue(all(c.kwargs['db'] is None for c in moment.find_moment.call_args_list))

    def test_user_failure_does_not_fallback_to_unrestricted_like(self):
        moment = Mock()
        moment.find_moment.side_effect = [RuntimeError('test failure'), SimpleNamespace(publisher='乙',text='')]
        moment.Like.return_value = Response(message='test')
        self.assertEqual(engine.run_selected_round(moment, ['甲','乙'], '', 5), 1)
        self.assertEqual([c.kwargs['publisher'] for c in moment.find_moment.call_args_list], ['甲','乙'])

    def test_upstream_substring_match_or_ignored_keyword_cannot_like_wrong_post(self):
        for item,keyword in [(SimpleNamespace(publisher='甲乙',text='关键词'),''),
                             (SimpleNamespace(publisher='甲',text='其他内容'),'关键词')]:
            moment=Mock()
            moment.find_moment.return_value=item
            self.assertEqual(engine.run_selected_round(moment,['甲'],keyword,5),1)
            moment.Like.assert_not_called()

    def test_multi_select_is_used_by_remote_runner_instead_of_legacy_single_name(self):
        cfg = config.validate_config({'replica_authors':['甲','乙'], 'replica_author':'旧用户'})
        runner = TaskRunner(lambda: cfg)
        process = Mock(pid=12345)
        process.poll.return_value = None
        with patch('console_app.tasks.subprocess.Popen', return_value=process) as spawn, patch.object(runner, '_read_loop'):
            self.assertTrue(runner.start('replica_like')[0])
        command = spawn.call_args.args[0]
        self.assertEqual(json.loads(command[command.index('--authors-json')+1]), ['甲','乙'])
        self.assertNotIn('旧用户', command)

    def test_config_normalizes_selection_and_rejects_non_names(self):
        self.assertEqual(config.validate_config({'replica_authors':['甲','甲',' 乙 ']})['replica_authors'], ['甲','乙'])
        for value in ('甲', [None], ['']):
            with self.assertRaises(config.ConfigError):
                config.validate_config({'replica_authors':value})

    def test_engine_constructor_does_not_initialize_database(self):
        fake = Mock(__version__='test')
        fake.WeChat.return_value.Moment.find_moment.return_value = SimpleNamespace(publisher='甲',text='')
        fake.WeChat.return_value.Moment.Like.return_value = Response(message='test')
        with patch.object(engine, '_import_with_watchdog', return_value=(fake,None)), patch('sys.argv',['engine','--author','甲']):
            self.assertEqual(engine.main(),0)
        db_adapter = fake.WeChat.call_args.kwargs['db']
        self.assertEqual(db_adapter.get_self_info(), {})
        fake.WeChatDB.assert_not_called()
        fake.MomentDB.assert_not_called()
