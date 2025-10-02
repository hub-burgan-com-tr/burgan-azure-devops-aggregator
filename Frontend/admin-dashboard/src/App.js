import React, { useState, useEffect } from 'react';
import {
  Layout,
  Menu,
  Card,
  Statistic,
  Table,
  Button,
  Space,
  Tag,
  Progress,
  Row,
  Col,
  Typography,
  Descriptions,
  Divider,
  Alert,
  Spin,
  notification,
  Badge,
  Avatar,
  Tooltip,
  Input,
  Modal,
  Form,
  Select,
  Switch,
  Popconfirm,
  message
} from 'antd';
import {
  DashboardOutlined,
  UserOutlined,
  CheckCircleOutlined,
  SettingOutlined,
  ReloadOutlined,
  PlusOutlined,
  EditOutlined,
  EyeOutlined,
  ExclamationCircleOutlined,
  DeleteOutlined,
  SearchOutlined,
  FilterOutlined,
  PlayCircleOutlined
} from '@ant-design/icons';
import './App.css';

const { Header, Sider, Content } = Layout;
const { Title, Text } = Typography;

// API Service
const API_BASE = process.env.REACT_APP_API_BASE || 'REACT_APP_API_BASE_PLACEHOLDER';

const apiService = {
  async fetchOverview() {
    try {
      const response = await fetch(`${API_BASE}/AdminDashboard/overview`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return await response.json();
    } catch (error) {
      console.error('Error fetching overview:', error);
      throw error;
    }
  },



  async fetchPendingReviews() {
    try {
      const response = await fetch(`${API_BASE}/ManualReview/pending`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return await response.json();
    } catch (error) {
      console.error('Error fetching pending reviews:', error);
      throw error;
    }
  },

  async fetchTemplates() {
    try {
      const response = await fetch(`${API_BASE}/AdminDashboard/templates`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return await response.json();
    } catch (error) {
      console.error('Error fetching templates:', error);
      throw error;
    }
  },

  async validateExpression(data) {
    try {
      const response = await fetch(`${API_BASE}/AdminDashboard/validate-expression`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return await response.json();
    } catch (error) {
      console.error('Error validating expression:', error);
      throw error;
    }
  },

  async fetchRules() {
    try {
      const response = await fetch(`${API_BASE}/AdminDashboard/rules`);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const rules = await response.json();
      return rules || [];
    } catch (error) {
      console.error('Error fetching rules:', error);
      throw error;
    }
  },

  async saveRule(ruleData) {
    try {
      const response = await fetch(`${API_BASE}/RulesExecute/save`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(ruleData),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return await response.json();
    } catch (error) {
      console.error('Error saving rule:', error);
      throw error;
    }
  },

  async deleteRule(ruleId) {
    try {
      const response = await fetch(`${API_BASE}/RulesExecute/delete/${ruleId}`, {
        method: 'DELETE',
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return true;
    } catch (error) {
      console.error('Error deleting rule:', error);
      throw error;
    }
  }
};

// Dashboard Components
const OverviewSection = ({ overview, onRefresh }) => {
  const statisticsData = [
    {
      title: 'Total Rules',
      value: overview.totalRules || 0,
      suffix: '',
      status: 'processing',
      color: '#1890ff'
    },
    {
      title: 'Active Rules',
      value: overview.activeRules || 0,
      suffix: '',
      status: 'success',
      color: '#52c41a'
    },
    {
      title: 'Pending Reviews',
      value: overview.pendingReviews || 0,
      suffix: '',
      status: 'warning',
      color: '#faad14'
    },
    {
      title: 'Success Rate',
      value: overview.conversionStats?.conversionRate || 0,
      suffix: '%',
      status: 'success',
      color: '#722ed1'
    }
  ];

  const recentActivity = overview.recentActivity || [];

  return (
    <div>
      <div style={{ marginBottom: 24, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Title level={2}>Dashboard Overview</Title>
        <Button 
          type="primary" 
          icon={<ReloadOutlined />} 
          onClick={onRefresh}
        >
          Refresh Data
        </Button>
      </div>

      <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
        {statisticsData.map((stat, index) => (
          <Col xs={24} sm={12} lg={6} key={index}>
            <Card>
              <Statistic
                title={stat.title}
                value={stat.value}
                suffix={stat.suffix}
                valueStyle={{ color: stat.color }}
              />
            </Card>
          </Col>
        ))}
      </Row>

      <Card title="Recent Activity" style={{ marginBottom: 24 }}>
        {recentActivity.length > 0 ? (
          <div>
            {recentActivity.map((activity, index) => (
              <div key={index} style={{ padding: '8px 0', borderBottom: index < recentActivity.length - 1 ? '1px solid #f0f0f0' : 'none' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <Text strong>{activity.action}</Text>
                    <Tag color="blue" style={{ marginLeft: 8 }}>{activity.target}</Tag>
                  </div>
                  <div style={{ textAlign: 'right' }}>
                    <div><Text type="secondary">{activity.user}</Text></div>
                    <div><Text type="secondary" style={{ fontSize: '12px' }}>
                      {new Date(activity.time).toLocaleTimeString()}
                    </Text></div>
                  </div>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <Text type="secondary">No recent activity</Text>
        )}
      </Card>
    </div>
  );
};



const PendingReviewsSection = ({ pendingReviews }) => {
  const reviews = pendingReviews.pendingReviews || [];

  const columns = [
    {
      title: 'Rule Name',
      dataIndex: 'ruleName',
      key: 'ruleName',
      render: (text) => <Text strong>{text}</Text>,
    },
    {
      title: 'Type',
      dataIndex: 'appliesTo',
      key: 'appliesTo',
      render: (text) => <Tag color="blue">{text}</Tag>,
    },
    {
      title: 'Rule Set',
      dataIndex: 'ruleSet',
      key: 'ruleSet',
      render: (text) => <Tag color="green">{text}</Tag>,
    },
    {
      title: 'Complexity',
      dataIndex: 'complexityReasons',
      key: 'complexityReasons',
      render: (reasons) => (
        <div>
          {(reasons || []).map((reason, index) => (
            <Tag key={index} color="orange" style={{ marginBottom: 2 }}>
              {reason}
            </Tag>
          ))}
        </div>
      ),
    },
    {
      title: 'Actions',
      key: 'actions',
      render: (_, record) => (
        <Space>
          <Tooltip title="Edit">
            <Button type="primary" ghost icon={<EditOutlined />} size="small" />
          </Tooltip>
          <Button type="primary" size="small">
            Approve
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: 24, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Title level={2}>Manual Review Queue</Title>
        <Space>
          <Button icon={<EyeOutlined />}>View All</Button>
          <Button type="primary" icon={<PlusOutlined />}>Bulk Approve</Button>
        </Space>
      </div>

      <Card>
        <div style={{ marginBottom: 16 }}>
          <Text>Pending Reviews: </Text>
          <Badge count={reviews.length} style={{ backgroundColor: '#faad14' }} />
        </div>
        
        {reviews.length > 0 ? (
          <Table 
            dataSource={reviews} 
            columns={columns} 
            rowKey={(record, index) => index}
            pagination={{ pageSize: 10 }}
          />
        ) : (
          <div style={{ textAlign: 'center', padding: '40px 0' }}>
            <CheckCircleOutlined style={{ fontSize: 48, color: '#52c41a', marginBottom: 16 }} />
            <div>
              <Title level={4}>All Caught Up!</Title>
              <Text type="secondary">No rules are waiting for manual review.</Text>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
};

const ExpressionValidator = () => {
  const [expression, setExpression] = useState('');
  const [validationResult, setValidationResult] = useState(null);
  const [loading, setLoading] = useState(false);

  const handleValidate = async () => {
    if (!expression.trim()) return;
    
    setLoading(true);
    try {
      const result = await apiService.validateExpression({
        expression,
        testWithSamples: true,
        samplePayloads: [
          {
            name: "Sample Test",
            data: {
              "System_State": "Active",
              "System_WorkItemType": "Task"
            }
          }
        ]
      });
      setValidationResult(result);
      
      notification.success({
        message: 'Validation Complete',
        description: result.isValid ? 'Expression is valid!' : 'Expression has validation errors.',
      });
    } catch (error) {
      console.error('Validation failed:', error);
      setValidationResult({
        isValid: false,
        validationMessages: ['Failed to validate expression']
      });
      
      notification.error({
        message: 'Validation Failed',
        description: 'Could not validate the expression. Please check your connection.',
      });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <Title level={2} style={{ marginBottom: 24 }}>Expression Validator</Title>
      
      <Card>
        <div style={{ marginBottom: 16 }}>
          <Text strong>Boolean Expression</Text>
          <div style={{ marginTop: 8 }}>
            <textarea
              value={expression}
              onChange={(e) => setExpression(e.target.value)}
              placeholder='body.Fields.System_State == "Active" && !string.IsNullOrWhiteSpace(body.Fields.System_Title)'
              style={{
                width: '100%',
                minHeight: 120,
                padding: 12,
                border: '1px solid #d9d9d9',
                borderRadius: 6,
                fontSize: 14,
                fontFamily: 'monospace'
              }}
            />
          </div>
        </div>
        
        <Button
          type="primary"
          icon={<CheckCircleOutlined />}
          onClick={handleValidate}
          disabled={!expression.trim()}
          loading={loading}
          size="large"
        >
          Validate Expression
        </Button>

        {validationResult && (
          <div style={{ marginTop: 24 }}>
            <Alert
              type={validationResult.isValid ? 'success' : 'error'}
              icon={validationResult.isValid ? <CheckCircleOutlined /> : <ExclamationCircleOutlined />}
              message={validationResult.isValid ? 'Valid Expression' : 'Invalid Expression'}
              description={
                <div>
                  {validationResult.validationMessages?.length > 0 && (
                    <div style={{ marginBottom: 12 }}>
                      <Text strong>Messages:</Text>
                      <ul style={{ margin: '4px 0', paddingLeft: 20 }}>
                        {validationResult.validationMessages.map((msg, idx) => (
                          <li key={idx}>{msg}</li>
                        ))}
                      </ul>
                    </div>
                  )}
                  
                  {validationResult.suggestions?.length > 0 && (
                    <div style={{ marginBottom: 12 }}>
                      <Text strong>Suggestions:</Text>
                      <ul style={{ margin: '4px 0', paddingLeft: 20 }}>
                        {validationResult.suggestions.map((suggestion, idx) => (
                          <li key={idx} style={{ color: '#1890ff' }}>{suggestion}</li>
                        ))}
                      </ul>
                    </div>
                  )}
                  
                  {validationResult.testResults?.length > 0 && (
                    <div>
                      <Text strong>Test Results:</Text>
                      <div style={{ marginTop: 8 }}>
                        {validationResult.testResults.map((test, idx) => (
                          <div key={idx} style={{ marginBottom: 4 }}>
                            {test.passed ? (
                              <CheckCircleOutlined style={{ color: '#52c41a', marginRight: 8 }} />
                            ) : (
                              <ExclamationCircleOutlined style={{ color: '#f5222d', marginRight: 8 }} />
                            )}
                            <Text>{test.sampleName}: {test.result}</Text>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              }
            />
          </div>
        )}
      </Card>
    </div>
  );
};

const RulesSection = ({ rules, onRefresh }) => {
  const [searchText, setSearchText] = useState('');
  const [filteredRules, setFilteredRules] = useState(rules);
  const [selectedRowKeys, setSelectedRowKeys] = useState([]);
  const [isModalVisible, setIsModalVisible] = useState(false);
  const [editingRule, setEditingRule] = useState(null);
  const [form] = Form.useForm();
  const [loading, setLoading] = useState(false);
  const [jsonPreviewVisible, setJsonPreviewVisible] = useState(false);
  const [generatedJson, setGeneratedJson] = useState('');
  const [detailModalVisible, setDetailModalVisible] = useState(false);
  const [selectedRule, setSelectedRule] = useState(null);

  // Filter rules based on search
  React.useEffect(() => {
    if (!searchText) {
      setFilteredRules(rules);
    } else {
      const filtered = rules.filter(rule => 
        rule.ruleName?.toLowerCase().includes(searchText.toLowerCase()) ||
        rule.ruleSet?.toLowerCase().includes(searchText.toLowerCase()) ||
        rule.appliesTo?.toLowerCase().includes(searchText.toLowerCase())
      );
      setFilteredRules(filtered);
    }
  }, [searchText, rules]);

  const handleAddRule = () => {
    setEditingRule(null);
    form.resetFields();
    setIsModalVisible(true);
  };

  const handleEditRule = (record) => {
    setEditingRule(record);
    form.setFieldsValue({
      ruleName: record.ruleName,
      expression: record.expression,
      appliesTo: record.appliesTo,
      ruleSet: record.ruleSet,
      isActive: record.isActive,
      priority: record.priority || 100,
      actions: record.actions || []
    });
    setIsModalVisible(true);
  };

  const handleViewDetails = (record) => {
    setSelectedRule(record);
    setDetailModalVisible(true);
  };

  const handleDeleteRule = async (ruleId) => {
    try {
      await apiService.deleteRule(ruleId);
      message.success('Rule deleted successfully');
      onRefresh();
    } catch (error) {
      message.error('Failed to delete rule');
    }
  };

  const handleToggleStatus = async (rule) => {
    try {
      const updatedRule = { ...rule, isActive: !rule.isActive };
      await apiService.saveRule(updatedRule);
      message.success(`Rule ${updatedRule.isActive ? 'activated' : 'deactivated'} successfully`);
      onRefresh();
    } catch (error) {
      message.error('Failed to update rule status');
    }
  };

  const handleModalSubmit = async (values) => {
    setLoading(true);
    try {
      const ruleData = {
        ...values,
        id: editingRule?.id,
        actions: editingRule?.actions || []
      };
      
      if (editingRule) {
        // Edit mode - save to database
        await apiService.saveRule(ruleData);
        message.success('Rule updated successfully');
        setIsModalVisible(false);
        form.resetFields();
        onRefresh();
      } else {
        // Add mode - show JSON preview
        const jsonPreview = {
          ruleName: values.ruleName,
          expression: values.expression,
          appliesTo: values.appliesTo,
          ruleSet: values.ruleSet,
          isActive: values.isActive || false,
          priority: values.priority || 100,
          actions: values.actions || []
        };
        
        // Show JSON preview modal
        setGeneratedJson(JSON.stringify(jsonPreview, null, 2));
        setJsonPreviewVisible(true);
        
        // Success notification
        message.success('JSON generated successfully!');
        
        setIsModalVisible(false);
        form.resetFields();
      }
    } catch (error) {
      if (editingRule) {
        message.error('Failed to update rule');
      } else {
        message.error('Failed to generate JSON preview');
      }
    } finally {
      setLoading(false);
    }
  };

  const columns = [
    {
      title: 'Rule Name',
      dataIndex: 'ruleName',
      key: 'ruleName',
      sorter: (a, b) => a.ruleName?.localeCompare(b.ruleName),
      render: (text) => <Text strong>{text}</Text>,
    },
    {
      title: 'Expression',
      dataIndex: 'expression',
      key: 'expression',
      ellipsis: true,
      render: (text) => (
        <Tooltip title={text}>
          <Text code style={{ fontSize: '12px' }}>
            {text?.length > 50 ? `${text.substring(0, 50)}...` : text}
          </Text>
        </Tooltip>
      ),
    },
    {
      title: 'Applies To',
      dataIndex: 'appliesTo',
      key: 'appliesTo',
      render: (text) => <Tag color="blue">{text}</Tag>,
    },
    {
      title: 'Rule Set',
      dataIndex: 'ruleSet',
      key: 'ruleSet',
      render: (text) => <Tag color="green">{text}</Tag>,
    },
    {
      title: 'Priority',
      dataIndex: 'priority',
      key: 'priority',
      sorter: (a, b) => (a.priority || 100) - (b.priority || 100),
      render: (priority) => <Badge count={priority || 100} style={{ backgroundColor: '#722ed1' }} />,
    },
    {
      title: 'Rule Actions',
      dataIndex: 'actions',
      key: 'actions',
      render: (actions) => (
        <Tooltip title={`${actions?.length || 0} action(s) configured`}>
          <Badge count={actions?.length || 0} style={{ backgroundColor: '#52c41a' }}>
            <Button type="text" icon={<PlayCircleOutlined />} size="small" />
          </Badge>
        </Tooltip>
      ),
    },
    {
      title: 'Status',
      dataIndex: 'isActive',
      key: 'isActive',
      render: (isActive, record) => (
        <Switch
          checked={isActive}
          onChange={() => handleToggleStatus(record)}
          checkedChildren="Active"
          unCheckedChildren="Inactive"
        />
      ),
    },
    {
      title: 'Actions',
      key: 'actions',
      render: (_, record) => (
        <Space>
          <Tooltip title="View Details">
            <Button icon={<EyeOutlined />} size="small" onClick={() => handleViewDetails(record)} />
          </Tooltip>
          <Tooltip title="Edit">
            <Button type="primary" ghost icon={<EditOutlined />} size="small" onClick={() => handleEditRule(record)} />
          </Tooltip>
          <Tooltip title="Test Rule">
            <Button icon={<PlayCircleOutlined />} size="small" />
          </Tooltip>
          <Popconfirm
            title="Are you sure you want to delete this rule?"
            onConfirm={() => handleDeleteRule(record.id)}
            okText="Yes"
            cancelText="No"
          >
            <Tooltip title="Delete">
              <Button danger icon={<DeleteOutlined />} size="small" />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const rowSelection = {
    selectedRowKeys,
    onChange: setSelectedRowKeys,
  };

  return (
    <div>
      <div style={{ marginBottom: 24, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Title level={2}>Rules Management</Title>
        <Space>
          <Button icon={<ReloadOutlined />} onClick={onRefresh}>
            Refresh
          </Button>
          <Button type="primary" icon={<PlusOutlined />} onClick={handleAddRule}>
            Add New Rule
          </Button>
          <Button 
            onClick={() => {
              const testJson = {
                ruleName: "Example Rule",
                expression: "body.Fields.System_State == \"Active\"",
                appliesTo: "Task",
                ruleSet: "Test Rules",
                isActive: true,
                priority: 100,
                actions: [
                  {
                    actionName: "ChangeState",
                    conditionType: "Success",
                    executionOrder: 1,
                    parameters: [
                      {
                        paramKey: "System.State",
                        paramValue: "In Progress"
                      }
                    ]
                  },
                  {
                    actionName: "AddComment",
                    conditionType: "Success",
                    executionOrder: 2,
                    parameters: [
                      {
                        paramKey: "CommentText",
                        paramValue: "Rule executed successfully"
                      }
                    ]
                  }
                ]
              };
              setGeneratedJson(JSON.stringify(testJson, null, 2));
              setJsonPreviewVisible(true);
              message.success('Example JSON with actions generated!');
            }}
          >
            🧪 Show Example JSON
          </Button>
        </Space>
      </div>

      <Card>
        <div style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div style={{ display: 'flex', gap: 16, alignItems: 'center' }}>
            <Input.Search
              placeholder="Search rules..."
              allowClear
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              style={{ width: 300 }}
              prefix={<SearchOutlined />}
            />
            <Badge count={filteredRules.length} style={{ backgroundColor: '#1890ff' }}>
              <Text>Total Rules</Text>
            </Badge>
          </div>
          
          {selectedRowKeys.length > 0 && (
            <Space>
              <Text>{selectedRowKeys.length} selected</Text>
              <Button type="primary" danger size="small">
                Bulk Delete
              </Button>
              <Button type="primary" size="small">
                Bulk Activate
              </Button>
            </Space>
          )}
        </div>

        <Table
          rowSelection={rowSelection}
          columns={columns}
          dataSource={filteredRules}
          rowKey="id"
          pagination={{
            pageSize: 10,
            showSizeChanger: true,
            showQuickJumper: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} of ${total} rules`,
          }}
          scroll={{ x: 1200 }}
        />
      </Card>

      <Modal
        title={editingRule ? 'Edit Rule' : 'Add New Rule'}
        open={isModalVisible}
        onCancel={() => setIsModalVisible(false)}
        onOk={() => form.submit()}
        confirmLoading={loading}
        width={1000}
        style={{ top: 20 }}
        bodyStyle={{ maxHeight: '70vh', overflowY: 'auto' }}
        okText={editingRule ? 'Update Rule' : 'Generate JSON'}
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={handleModalSubmit}
        >
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item
                name="ruleName"
                label="Rule Name"
                rules={[{ required: true, message: 'Please enter rule name' }]}
              >
                <Input placeholder="Enter rule name" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="priority"
                label="Priority"
                rules={[{ required: true, message: 'Please enter priority' }]}
              >
                <Input type="number" placeholder="100" />
              </Form.Item>
            </Col>
          </Row>

          <Form.Item
            name="expression"
            label="Rule Expression"
            rules={[{ required: true, message: 'Please enter rule expression' }]}
          >
            <Input.TextArea
              rows={4}
              placeholder='body.Fields.System_State == "Active" && !string.IsNullOrWhiteSpace(body.Fields.System_Title)'
              style={{ fontFamily: 'monospace' }}
            />
          </Form.Item>

          <Row gutter={16}>
            <Col span={12}>
              <Form.Item
                name="appliesTo"
                label="Applies To"
                rules={[{ required: true, message: 'Please select applies to' }]}
              >
                <Select placeholder="Select work item type">
                  <Select.Option value="Task">Task</Select.Option>
                  <Select.Option value="Bug">Bug</Select.Option>
                  <Select.Option value="User Story">User Story</Select.Option>
                  <Select.Option value="Epic">Epic</Select.Option>
                  <Select.Option value="Feature">Feature</Select.Option>
                  <Select.Option value="Geliştirme Talebi">Geliştirme Talebi</Select.Option>
                </Select>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="ruleSet"
                label="Rule Set"
                rules={[{ required: true, message: 'Please enter rule set' }]}
              >
                <Input placeholder="Enter rule set name" />
              </Form.Item>
            </Col>
          </Row>

                    <Form.Item
            name="isActive"
            label="Active"
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>

          <Divider orientation="left">Actions</Divider>
          
          <Form.List name="actions">
            {(fields, { add, remove }) => (
              <>
                {fields.map(({ key, name, ...restField }) => (
                  <Card 
                    key={key} 
                    size="small" 
                    style={{ marginBottom: 16, backgroundColor: '#fafafa' }}
                    title={`Action ${name + 1}`}
                    extra={
                      <Button 
                        type="text" 
                        danger 
                        icon={<DeleteOutlined />} 
                        onClick={() => remove(name)}
                      />
                    }
                  >
                    <Row gutter={16}>
                      <Col span={8}>
                        <Form.Item
                          {...restField}
                          name={[name, 'actionName']}
                          label="Action Type"
                          rules={[{ required: true, message: 'Please select action type' }]}
                        >
                          <Select placeholder="Select action">
                            <Select.Option value="ChangeState">Change State</Select.Option>
                            <Select.Option value="AddComment">Add Comment</Select.Option>
                            <Select.Option value="SetField">Set Field</Select.Option>
                            <Select.Option value="UpdateField">Update Field</Select.Option>
                            <Select.Option value="TransitionToState">Transition To State</Select.Option>
                          </Select>
                        </Form.Item>
                      </Col>
                      <Col span={8}>
                        <Form.Item
                          {...restField}
                          name={[name, 'conditionType']}
                          label="Condition Type"
                          rules={[{ required: true, message: 'Please select condition' }]}
                        >
                          <Select placeholder="Select condition">
                            <Select.Option value="Success">Success</Select.Option>
                            <Select.Option value="Failure">Failure</Select.Option>
                          </Select>
                        </Form.Item>
                      </Col>
                      <Col span={8}>
                        <Form.Item
                          {...restField}
                          name={[name, 'executionOrder']}
                          label="Execution Order"
                          rules={[{ required: true, message: 'Please enter order' }]}
                        >
                          <Input type="number" placeholder="1" />
                        </Form.Item>
                      </Col>
                    </Row>

                    <Form.List name={[name, 'parameters']}>
                      {(paramFields, { add: addParam, remove: removeParam }) => (
                        <>
                          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                            <Text strong>Parameters:</Text>
                            <Button 
                              type="dashed" 
                              size="small"
                              icon={<PlusOutlined />}
                              onClick={() => addParam()}
                            >
                              Add Parameter
                            </Button>
                          </div>
                          {paramFields.map(({ key: paramKey, name: paramName, ...paramRestField }) => (
                            <Row key={paramKey} gutter={8} style={{ marginBottom: 8 }}>
                              <Col span={10}>
                                <Form.Item
                                  {...paramRestField}
                                  name={[paramName, 'paramKey']}
                                  rules={[{ required: true, message: 'Key required' }]}
                                >
                                  <Input placeholder="Parameter Key" />
                                </Form.Item>
                              </Col>
                              <Col span={12}>
                                <Form.Item
                                  {...paramRestField}
                                  name={[paramName, 'paramValue']}
                                  rules={[{ required: true, message: 'Value required' }]}
                                >
                                  <Input placeholder="Parameter Value" />
                                </Form.Item>
                              </Col>
                              <Col span={2}>
                                <Button 
                                  type="text" 
                                  danger 
                                  icon={<DeleteOutlined />} 
                                  onClick={() => removeParam(paramName)}
                                />
                              </Col>
                            </Row>
                          ))}
                        </>
                      )}
                    </Form.List>
                  </Card>
                ))}
                <Form.Item>
                  <Button 
                    type="dashed" 
                    onClick={() => add({ conditionType: 'Success', executionOrder: fields.length + 1 })} 
                    block 
                    icon={<PlusOutlined />}
                  >
                    Add Action
                  </Button>
                </Form.Item>
              </>
            )}
          </Form.List>
        </Form>
      </Modal>

                  {/* JSON Preview Modal */}
          <Modal
            title="Rule JSON Preview"
            open={jsonPreviewVisible}
            onCancel={() => setJsonPreviewVisible(false)}
            width={800}
            destroyOnClose={true}
            footer={[
              <Button key="close" onClick={() => setJsonPreviewVisible(false)}>
                Close
              </Button>,
              <Button 
                key="copy" 
                type="primary" 
                onClick={() => {
                  navigator.clipboard.writeText(generatedJson)
                    .then(() => {
                      message.success('JSON copied to clipboard!');
                      setJsonPreviewVisible(false);
                    })
                    .catch(() => message.info('Please copy the JSON manually'));
                }}
              >
                📋 Copy & Close
              </Button>
            ]}
          >
            <div>
              <p style={{ marginBottom: 16 }}>
                <strong>Generated Rule JSON:</strong> Copy this JSON and use it with your save API endpoint:
              </p>
              <Input.TextArea
                rows={15}
                value={generatedJson}
                style={{ 
                  fontFamily: 'monospace',
                  fontSize: '12px',
                  backgroundColor: '#f5f5f5'
                }}
                readOnly
              />
              <div style={{ marginTop: 16, padding: 12, backgroundColor: '#e6f7ff', borderRadius: 6 }}>
                <Text strong style={{ color: '#1890ff' }}>API Endpoint:</Text>
                <br />
                <Text code>POST /api/RulesExecute/save</Text>
                <br />
                <Text type="secondary" style={{ fontSize: '12px' }}>
                  Use this JSON as the request body to save the rule to your database.
                </Text>
              </div>
            </div>
          </Modal>

          {/* Rule Details Modal */}
          <Modal
            title={
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <span style={{ fontSize: '20px' }}>📋</span>
                <span>Rule Details</span>
                {selectedRule && (
                  <Tag color={selectedRule.isActive ? 'green' : 'red'}>
                    {selectedRule.isActive ? 'ACTIVE' : 'INACTIVE'}
                  </Tag>
                )}
              </div>
            }
            open={detailModalVisible}
            onCancel={() => setDetailModalVisible(false)}
            width={900}
            footer={[
              <Button key="close" onClick={() => setDetailModalVisible(false)}>
                Close
              </Button>,
              <Button key="edit" type="primary" onClick={() => {
                setDetailModalVisible(false);
                handleEditRule(selectedRule);
              }}>
                Edit Rule
              </Button>
            ]}
          >
            {selectedRule && (
              <div>
                {/* Basic Rule Info */}
                <Descriptions 
                  title="Basic Information" 
                  bordered 
                  column={2}
                  style={{ marginBottom: 24 }}
                >
                  <Descriptions.Item label="Rule ID" span={1}>
                    <Tag color="blue">{selectedRule.ruleId}</Tag>
                  </Descriptions.Item>
                  <Descriptions.Item label="Priority" span={1}>
                    <Badge count={selectedRule.priority || 100} style={{ backgroundColor: '#722ed1' }} />
                  </Descriptions.Item>
                  <Descriptions.Item label="Rule Name" span={2}>
                    <Text strong style={{ fontSize: '16px' }}>{selectedRule.ruleName}</Text>
                  </Descriptions.Item>
                  <Descriptions.Item label="Applies To" span={1}>
                    <Tag color="cyan">{selectedRule.appliesTo}</Tag>
                  </Descriptions.Item>
                  <Descriptions.Item label="Rule Set" span={1}>
                    <Tag color="purple">{selectedRule.ruleSet}</Tag>
                  </Descriptions.Item>
                </Descriptions>

                {/* Expression */}
                <Card 
                  title="📝 Rule Expression" 
                  size="small" 
                  style={{ marginBottom: 24 }}
                >
                  <Input.TextArea
                    value={selectedRule.expression}
                    rows={4}
                    readOnly
                    style={{
                      fontFamily: 'monospace',
                      fontSize: '13px',
                      backgroundColor: '#f8f9fa',
                      border: 'none'
                    }}
                  />
                </Card>

                {/* Actions */}
                <Card title="⚡ Rule Actions" size="small">
                  {selectedRule.actions && selectedRule.actions.length > 0 ? (
                    <div>
                      {selectedRule.actions.map((action, index) => (
                        <Card 
                          key={index}
                          size="small" 
                          style={{ 
                            marginBottom: 16, 
                            backgroundColor: '#fafafa',
                            border: '1px solid #e8e8e8'
                          }}
                          title={
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                              <span>🎯 Action {index + 1}: {action.actionName}</span>
                              <Space>
                                <Tag color={action.conditionType === 'Success' ? 'green' : 'orange'}>
                                  {action.conditionType}
                                </Tag>
                                <Tag color="blue">Order: {action.executionOrder}</Tag>
                              </Space>
                            </div>
                          }
                        >
                          {action.parameters && action.parameters.length > 0 && (
                            <div>
                              <Text strong style={{ marginBottom: 8, display: 'block' }}>Parameters:</Text>
                              <Row gutter={[16, 8]}>
                                {action.parameters.map((param, paramIndex) => (
                                  <Col span={24} key={paramIndex}>
                                    <div style={{ 
                                      padding: 8, 
                                      backgroundColor: '#ffffff', 
                                      border: '1px solid #d9d9d9',
                                      borderRadius: 4,
                                      display: 'flex',
                                      alignItems: 'center',
                                      gap: 12
                                    }}>
                                      <Text code style={{ minWidth: '120px', fontWeight: 'bold' }}>
                                        {param.paramKey}
                                      </Text>
                                      <span>→</span>
                                      <Text style={{ flex: 1 }}>
                                        {param.paramValue}
                                      </Text>
                                    </div>
                                  </Col>
                                ))}
                              </Row>
                            </div>
                          )}
                        </Card>
                      ))}
                    </div>
                  ) : (
                    <div style={{ textAlign: 'center', padding: 24, color: '#999' }}>
                      <div style={{ fontSize: '48px', marginBottom: 16 }}>⚠️</div>
                      <p>No actions configured for this rule</p>
                    </div>
                  )}
                </Card>
              </div>
            )}
          </Modal>
        </div>
      );
    };

const TemplatesSection = ({ templates }) => {
  const templateList = templates.templates || [];

  return (
    <div>
      <Title level={2} style={{ marginBottom: 24 }}>Rule Templates</Title>
      
      <Row gutter={[16, 16]}>
        {templateList.map((template, index) => (
          <Col xs={24} md={12} lg={8} key={index}>
            <Card
              title={template.name}
              extra={<Tag color="blue">{template.category}</Tag>}
              actions={[
                <Button type="link" icon={<EyeOutlined />}>View</Button>,
                <Button type="primary" icon={<PlusOutlined />}>Use Template</Button>
              ]}
            >
              <Text type="secondary">{template.description}</Text>
            </Card>
          </Col>
        ))}
        
        {templateList.length === 0 && (
          <Col span={24}>
            <Card style={{ textAlign: 'center', padding: '40px 0' }}>
              <SettingOutlined style={{ fontSize: 48, color: '#d9d9d9', marginBottom: 16 }} />
              <div>
                <Title level={4}>No Templates Available</Title>
                <Text type="secondary">Templates will appear here when available.</Text>
              </div>
            </Card>
          </Col>
        )}
      </Row>
    </div>
  );
};

// Main App Component
function App() {
  const [collapsed, setCollapsed] = useState(false);
  const [selectedKey, setSelectedKey] = useState('overview');
  const [overview, setOverview] = useState({});

  const [pendingReviews, setPendingReviews] = useState({});
  const [templates, setTemplates] = useState({});
  const [rules, setRules] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const menuItems = [
    {
      key: 'overview',
      icon: <DashboardOutlined />,
      label: 'Overview',
    },
    {
      key: 'rules',
      icon: <FilterOutlined />,
      label: 'Rules',
    },

    {
      key: 'reviews',
      icon: <UserOutlined />,
      label: 'Manual Reviews',
    },
    {
      key: 'validator',
      icon: <CheckCircleOutlined />,
      label: 'Expression Validator',
    },
    {
      key: 'templates',
      icon: <SettingOutlined />,
      label: 'Templates',
    },
  ];

  const fetchData = async () => {
    setLoading(true);
    setError(null);
    
    try {
      const [overviewData, reviewsData, templatesData, rulesData] = await Promise.all([
        apiService.fetchOverview().catch(() => ({})),
        apiService.fetchPendingReviews().catch(() => ({})),
        apiService.fetchTemplates().catch(() => ({})),
        apiService.fetchRules().catch(() => ([]))
      ]);

      setOverview(overviewData);
      setPendingReviews(reviewsData);
      setTemplates(templatesData);
      setRules(rulesData);
    } catch (err) {
      console.error('Error fetching data:', err);
      setError('Failed to connect to backend API. Please ensure the backend is running on https://localhost:7232');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
  }, []);

  const renderContent = () => {
    if (loading) {
      return (
        <div style={{ 
          display: 'flex', 
          justifyContent: 'center', 
          alignItems: 'center', 
          minHeight: '400px' 
        }}>
          <Spin size="large" />
        </div>
      );
    }

    if (error) {
      return (
        <Alert
          message="Connection Error"
          description={error}
          type="error"
          action={
            <Button size="small" danger onClick={fetchData}>
              Retry
            </Button>
          }
        />
      );
    }

    switch (selectedKey) {
      case 'overview':
        return <OverviewSection overview={overview} onRefresh={fetchData} />;
      case 'rules':
        return <RulesSection rules={rules} onRefresh={fetchData} />;

      case 'reviews':
        return <PendingReviewsSection pendingReviews={pendingReviews} />;
      case 'validator':
        return <ExpressionValidator />;
      case 'templates':
        return <TemplatesSection templates={templates} />;
      default:
        return <OverviewSection overview={overview} onRefresh={fetchData} />;
    }
  };

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider 
        collapsible 
        collapsed={collapsed} 
        onCollapse={setCollapsed}
        style={{
          background: '#fff',
          boxShadow: '2px 0 8px rgba(0,0,0,0.15)'
        }}
      >
        <div style={{ 
          height: 64, 
          margin: 16, 
          display: 'flex', 
          alignItems: 'center',
          justifyContent: 'center'
        }}>
          <Title level={collapsed ? 5 : 4} style={{ margin: 0, color: '#1890ff' }}>
            {collapsed ? 'RD' : 'Rules Dashboard'}
          </Title>
        </div>
        
        <Menu
          theme="light"
          mode="inline"
          selectedKeys={[selectedKey]}
          items={menuItems}
          onClick={({ key }) => setSelectedKey(key)}
        />
      </Sider>
      
      <Layout>
        <Header style={{ 
          background: '#fff', 
          padding: '0 24px',
          boxShadow: '0 2px 8px rgba(0,0,0,0.15)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between'
        }}>
          <Title level={3} style={{ margin: 0 }}>
            Admin Dashboard
          </Title>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
            <Text type="secondary">
              Last updated: {new Date().toLocaleTimeString()}
            </Text>
            <Avatar icon={<UserOutlined />} />
          </div>
        </Header>
        
        <Content style={{ 
          margin: 24, 
          padding: 24, 
          background: '#f0f2f5',
          borderRadius: 8 
        }}>
          {renderContent()}
        </Content>
      </Layout>
    </Layout>
  );
}

export default App;
